using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class RecommendationNotifierTests : LocalDataTest
{
    private readonly FakeNotifications _notifications = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeSync _sync = new();
    private readonly RecommendationStore _store;
    private readonly RecommendationNotifier _notifier;

    public RecommendationNotifierTests()
    {
        _store = new RecommendationStore(Database);
        _notifier = new RecommendationNotifier(_store, _settings, _notifications, _sync, Clock);
    }

    [Fact]
    public async Task A_new_recommendation_is_announced_only_once()
    {
        await SaveAsync("Hidrátate mejor", Clock.GetUtcNow().AddMinutes(-5));

        await _notifier.CheckAsync();
        await _notifier.CheckAsync();

        Assert.Equal(["Hidrátate mejor"], _notifications.Shown);
    }

    [Fact]
    public async Task Only_the_newest_is_announced_when_several_arrive()
    {
        _settings.LastNotifiedRecommendationAt = Clock.GetUtcNow().AddDays(-3);
        await SaveAsync("Anteayer", Clock.GetUtcNow().AddDays(-2));
        await SaveAsync("Hoy", Clock.GetUtcNow().AddHours(-1));

        await _notifier.CheckAsync();

        Assert.Equal(["Hoy"], _notifications.Shown);
    }

    [Fact]
    public async Task Old_recommendations_are_not_announced_on_a_fresh_install()
    {
        await SaveAsync("De la semana pasada", Clock.GetUtcNow().AddDays(-7));

        await _notifier.CheckAsync();

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task Each_sync_checks_for_new_recommendations()
    {
        await SaveAsync("Duerme a la misma hora", Clock.GetUtcNow());

        _sync.RaiseCompleted();
        await _notifier.CheckAsync(); // Espera a que termine la revisión que lanzó la sincronización.

        Assert.Equal(["Duerme a la misma hora"], _notifications.Shown);
    }

    private Task SaveAsync(string title, DateTimeOffset createdAt) =>
        _store.SaveAsync([new RecommendationRow { Id = Guid.NewGuid(), Title = title, Summary = "Resumen", CreatedAt = createdAt }]);

    private sealed class FakeNotifications : IAppNotifications
    {
        public List<string> Shown { get; } = [];

        public Task<bool> EnsurePermissionAsync() => Task.FromResult(true);

        public void ShowRecommendation(string title, string text) => Shown.Add(title);
    }

    private sealed class FakeSync : ISyncService
    {
        public event EventHandler<SyncCompletedEventArgs>? Completed;

        public Task<int> PendingCountAsync() => Task.FromResult(0);

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RequestSync()
        {
        }

        public void RaiseCompleted() => Completed?.Invoke(this, new SyncCompletedEventArgs([]));
    }
}

public sealed class PresentationModeTests
{
    private readonly FakeSettings _settings = new();
    private readonly FakeSchedule _schedule = new();
    private readonly PresentationMode _mode;

    public PresentationModeTests() => _mode = new PresentationMode(_settings, _schedule);

    [Fact]
    public void Turning_it_on_and_off_saves_the_choice_and_moves_the_alarm()
    {
        _mode.Enable();
        Assert.True(_settings.PresentationMode);
        Assert.True(_schedule.Running);

        _mode.Disable();
        Assert.False(_settings.PresentationMode);
        Assert.False(_schedule.Running);
    }

    [Fact]
    public void Opening_the_app_rearms_the_alarm_only_if_it_was_on()
    {
        _mode.Restore();
        Assert.False(_schedule.Running);

        _settings.PresentationMode = true;
        _mode.Restore();
        Assert.True(_schedule.Running);
    }

    private sealed class FakeSchedule : IPresentationSchedule
    {
        public bool Running { get; private set; }

        public bool IsPrecise => true;

        public void Start() => Running = true;

        public void Stop() => Running = false;

        public void OpenPrecisionSettings()
        {
        }
    }
}
