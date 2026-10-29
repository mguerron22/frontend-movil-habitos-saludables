using System.Net;
using System.Net.Http.Json;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class SyncServiceTests : LocalDataTest
{
    private static readonly DateTimeOffset ServerTime = new(2026, 10, 28, 17, 5, 0, TimeSpan.Zero);

    private readonly FakeServer _server = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly SyncService _sync;
    private readonly HabitService _habits;
    private readonly RecommendationStore _recommendations;
    private SyncPushRequest? _lastPush;
    private string? _lastPullQuery;

    public SyncServiceTests()
    {
        _recommendations = new RecommendationStore(Database);
        var client = new AuthenticatedApiClient(new HttpClient(_server), _connectivity, new FakeSettings());
        _sync = new SyncService(
            new SyncApi(client),
            Store,
            _recommendations,
            Outbox,
            _connectivity,
            new OnlineSession(),
            new FakeSettings(),
            NullLogger<SyncService>.Instance);
        _habits = new HabitService(Store, Outbox, new NoSync(), Clock);

        RespondToPush(_ => new SyncPushResponse(ServerTime, [], []));
        RespondToPull(new SyncPullResponse(ServerTime, false, [], []));
    }

    [Fact]
    public async Task Applied_changes_leave_the_queue()
    {
        var habit = await _habits.SaveAsync(Draft("Tomar agua"));
        RespondToPush(request => new SyncPushResponse(ServerTime, request.Habits.Select(Applied).ToList(), []));
        RespondToPull(new SyncPullResponse(ServerTime, false, [new(Habit(habit.Id, "Tomar agua"), false)], []));

        await _sync.SyncAsync();

        Assert.Equal(0, await Outbox.CountAsync());
        Assert.Equal(habit.Id, Assert.Single(_lastPush!.Habits).Id);
        Assert.NotNull(await Store.FindHabitAsync(habit.Id));
    }

    [Fact]
    public async Task Confirmed_deletion_removes_the_row_for_good()
    {
        var habit = await _habits.SaveAsync(Draft("Tomar agua"));
        await _habits.DeleteAsync(habit.Id);
        RespondToPush(request => new SyncPushResponse(ServerTime, request.Habits.Select(Applied).ToList(), []));

        await _sync.SyncAsync();

        Assert.True(Assert.Single(_lastPush!.Habits).IsDeleted);
        Assert.Null(await Store.FindHabitAsync(habit.Id));
    }

    [Fact]
    public async Task Rejected_change_is_reported_and_forces_a_full_download()
    {
        var habit = await _habits.SaveAsync(Draft("Leer"));
        RespondToPush(request => new SyncPushResponse(
            ServerTime,
            [new SyncItemResult(habit.Id, SyncItemStatus.Rejected, null, "Ya existe un hábito con ese nombre.")],
            []));
        SyncCompletedEventArgs? completed = null;
        _sync.Completed += (_, args) => completed = args;

        await _sync.SyncAsync();

        Assert.Equal(["Leer: Ya existe un hábito con ese nombre."], completed!.Rejected);
        Assert.Equal("", _lastPullQuery); // sin "since": descarga completa
        Assert.Null(await Store.FindHabitAsync(habit.Id)); // el servidor no lo tiene y ya no está pendiente
    }

    [Fact]
    public async Task Pull_brings_server_changes_but_never_overwrites_pending_local_ones()
    {
        var local = await _habits.SaveAsync(Draft("Mío sin enviar"));
        var remote = Habit(Guid.NewGuid(), "Creado en otro teléfono");
        // El servidor no menciona el hábito local: sigue pendiente para el próximo envío.
        RespondToPush(_ => new SyncPushResponse(ServerTime, [], []));
        RespondToPull(new SyncPullResponse(ServerTime, false, [new(remote, false), new(Habit(local.Id, "Versión vieja"), false)], []));

        await _sync.SyncAsync();

        Assert.Equal("Creado en otro teléfono", (await Store.FindHabitAsync(remote.Id))!.Name);
        Assert.Equal("Mío sin enviar", (await Store.FindHabitAsync(local.Id))!.Name);
        Assert.True(await Outbox.IsPendingAsync(PendingEntity.Habit, local.Id));
    }

    [Fact]
    public async Task Same_day_recorded_on_another_phone_replaces_the_local_row()
    {
        var habit = Habit(Guid.NewGuid(), "Tomar agua");
        await Store.SaveHabitAsync(SyncService.ToRow(habit));
        var localRecordId = Guid.NewGuid();
        await Store.SaveRecordAsync(new HabitRecordRow { Id = localRecordId, HabitId = habit.Id, RecordDate = HabitRecordRow.Format(Today) });
        var serverRecord = new HabitRecordResponse(Guid.NewGuid(), habit.Id, Today, 8, true, null, null, null, ServerTime);
        RespondToPull(new SyncPullResponse(ServerTime, false, [], [new(serverRecord, false)]));

        await _sync.SyncAsync();

        Assert.Null(await Store.FindRecordAsync(localRecordId));
        Assert.True((await Store.FindRecordAsync(habit.Id, Today))!.Completed);
    }

    [Fact]
    public async Task Incremental_pull_uses_the_saved_cursor()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();

        Assert.Contains("since=", _lastPullQuery, StringComparison.Ordinal);
        Assert.Equal(ServerTime, await Outbox.GetPullCursorAsync());
    }

    [Fact]
    public async Task Pull_keeps_the_ai_recommendations_to_read_them_offline()
    {
        var older = Recommendation("Semana constante", ServerTime.AddDays(-7));
        var latest = Recommendation("Cuida los lunes", ServerTime);
        RespondToPull(new SyncPullResponse(ServerTime, false, [], [], [older, latest]));

        await _sync.SyncAsync();
        RespondToPull(new SyncPullResponse(ServerTime, false, [], [], [latest]));
        await _sync.SyncAsync();

        var saved = await _recommendations.GetAllAsync();
        Assert.Equal(["Cuida los lunes", "Semana constante"], saved.Select(row => row.Title));
    }

    [Fact]
    public async Task Without_connection_nothing_is_sent_and_changes_wait()
    {
        await _habits.SaveAsync(Draft("Tomar agua"));
        _connectivity.IsOnline = false;

        await _sync.SyncAsync();

        Assert.Null(_lastPush);
        Assert.Equal(1, await Outbox.CountAsync());
    }

    private static HabitDraft Draft(string name) => new(null, name, HabitCategory.Salud, 1, "vez", [], TrackingMode.Manual);

    private static HabitResponse Habit(Guid id, string name) =>
        new(id, name, HabitCategory.Salud, 1, "vez", [], TrackingMode.Manual, true, ServerTime, ServerTime);

    private static RecommendationResponse Recommendation(string title, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), title, "Resumen", "Tomar agua", "• Acción 1\n• Acción 2","openai/gpt-oss-120b",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 28), createdAt);

    private static SyncItemResult Applied(HabitChange change) => new(change.Id, SyncItemStatus.Applied, null, null);

    private void RespondToPush(Func<SyncPushRequest, SyncPushResponse> respond) =>
        _server.On("/api/sync/push", async request =>
        {
            _lastPush = await request.Content!.ReadFromJsonAsync<SyncPushRequest>(ApiClient.JsonOptions);
            return FakeServer.Json(HttpStatusCode.OK, respond(_lastPush!));
        });

    private void RespondToPull(SyncPullResponse response) =>
        _server.On("/api/sync/pull", request =>
        {
            _lastPullQuery = request.RequestUri!.Query;
            // Como el backend: sin "since" la respuesta es una descarga completa.
            var full = response with { IsFullSnapshot = string.IsNullOrEmpty(_lastPullQuery) };
            return Task.FromResult(FakeServer.Json(HttpStatusCode.OK, full));
        });

    private sealed class OnlineSession : ISessionManager
    {
        public Models.UserProfile? CurrentUser { get; } = Samples.User();

        public bool IsOfflineSession => false;

        public event EventHandler<SessionEndedEventArgs>? SessionEnded { add { } remove { } }

        public event EventHandler? OnlineRestored { add { } remove { } }

        public Task<bool> HasStoredSessionAsync() => Task.FromResult(true);

        public Task UpdateCurrentUserAsync(Models.UserProfile user) => Task.CompletedTask;

        public Task LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RegisterAsync(NewAccount account, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(ResumeOutcome.Online);

        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token");

        public Task<string> RenewAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken) => Task.FromResult("token");
    }

    private sealed class NoSync : ISyncService
    {
        public event EventHandler<SyncCompletedEventArgs>? Completed { add { } remove { } }

        public Task<int> PendingCountAsync() => Task.FromResult(0);

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RequestSync()
        {
        }
    }
}
