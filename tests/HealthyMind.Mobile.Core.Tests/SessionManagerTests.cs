using System.Net;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class SessionManagerTests : IDisposable
{
    private readonly FakeServer _server = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeTokenStore _tokens = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly ManualClock _clock = new(Samples.Now);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"hm-tests-{Guid.NewGuid():N}.db3");
    private readonly LocalDatabase _database;
    private readonly SessionManager _session;

    public SessionManagerTests()
    {
        _database = new LocalDatabase(_databasePath);
        var client = new AnonymousApiClient(new HttpClient(_server), _connectivity, _settings);
        _session = new SessionManager(
            new AuthApi(client),
            _tokens,
            _settings,
            new UserCache(_database, _clock),
            _database,
            _connectivity,
            _clock,
            NullLogger<SessionManager>.Instance);
    }

    [Fact]
    public async Task Login_keeps_refresh_token_and_profile_for_later()
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));

        await _session.LoginAsync("ana.perez", "Clave1234");

        Assert.Equal("refresh-1", _tokens.RefreshToken);
        Assert.Equal("Ana", _session.CurrentUser?.FirstName);
        Assert.Equal("access-1", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.True(await _session.HasStoredSessionAsync());
    }

    [Fact]
    public async Task Parallel_requests_with_expired_token_trigger_a_single_refresh()
    {
        // El backend corta el dispositivo si recibe dos refresh con el mismo token: solo debe salir uno.
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        _server.On("/api/auth/refresh", async _ =>
        {
            await Task.Delay(100);
            return FakeServer.Json(HttpStatusCode.OK, Samples.Auth("access-2", "refresh-2", accessLifetime: TimeSpan.FromHours(1)));
        });
        await _session.LoginAsync("ana", "Clave1234");
        _clock.Advance(TimeSpan.FromMinutes(20));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _session.GetAccessTokenAsync(CancellationToken.None)));

        Assert.All(tokens, token => Assert.Equal("access-2", token));
        Assert.Equal(1, _server.Calls("/api/auth/refresh"));
        Assert.Equal("refresh-2", _tokens.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SessionEndReason.Expired)]
    [InlineData(HttpStatusCode.Forbidden, SessionEndReason.Suspended)]
    public async Task Rejected_refresh_ends_the_session(HttpStatusCode status, SessionEndReason expected)
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        _server.On("/api/auth/refresh", status, new { title = "x", detail = "rechazado" });
        await _session.LoginAsync("ana", "Clave1234");
        _clock.Advance(TimeSpan.FromMinutes(20));
        SessionEndedEventArgs? ended = null;
        _session.SessionEnded += (_, args) => ended = args;

        var error = await Assert.ThrowsAsync<SessionExpiredException>(() => _session.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(expected, error.Reason);
        Assert.Equal(expected, ended?.Reason);
        Assert.Null(_tokens.RefreshToken);
        Assert.Null(_session.CurrentUser);
    }

    [Fact]
    public async Task Resume_without_server_enters_offline_with_the_saved_profile()
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        await _session.LoginAsync("ana", "Clave1234");
        _connectivity.ServerReachable = false;

        var outcome = await _session.ResumeAsync();

        Assert.Equal(ResumeOutcome.Offline, outcome);
        Assert.True(_session.IsOfflineSession);
        Assert.Equal("Ana", _session.CurrentUser?.FirstName);
        Assert.Equal(0, _server.Calls("/api/auth/refresh"));
    }

    [Theory]
    [InlineData(0, "refresh-1")]
    [InlineData(20, "refresh-2")]
    public async Task Offline_session_goes_online_by_itself_when_connection_returns(int minutesOffline, string expectedRefreshToken)
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        _server.On("/api/auth/refresh", HttpStatusCode.OK, Samples.Auth("access-2", "refresh-2"));
        await _session.LoginAsync("ana", "Clave1234");
        _connectivity.ServerReachable = false;
        await _session.ResumeAsync();
        _clock.Advance(TimeSpan.FromMinutes(minutesOffline));
        var restored = new TaskCompletionSource();
        _session.OnlineRestored += (_, _) => restored.TrySetResult();

        _connectivity.Raise(online: true);
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_session.IsOfflineSession);
        Assert.Equal(expectedRefreshToken, _tokens.RefreshToken);
    }

    [Fact]
    public async Task Without_stored_session_resume_asks_for_password()
    {
        Assert.Equal(ResumeOutcome.NoSession, await _session.ResumeAsync());
    }

    [Fact]
    public async Task Logout_clears_tokens_biometrics_and_local_data()
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        _server.On("/api/auth/logout", HttpStatusCode.NoContent);
        await _session.LoginAsync("ana", "Clave1234");
        _settings.BiometricEnabled = true;

        await _session.LogoutAsync();

        Assert.Null(_tokens.RefreshToken);
        Assert.False(_settings.BiometricEnabled);
        Assert.Null(await new UserCache(_database, _clock).GetAsync());
        Assert.Equal(1, _server.Calls("/api/auth/logout"));
    }

    [Fact]
    public async Task Another_user_on_the_same_phone_does_not_inherit_biometrics()
    {
        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-1", "refresh-1"));
        await _session.LoginAsync("ana", "Clave1234");
        _settings.BiometricEnabled = true;

        _server.On("/api/auth/login", HttpStatusCode.OK, Samples.Auth("access-9", "refresh-9", Samples.User(Guid.NewGuid())));
        await _session.LoginAsync("otro", "Clave1234");

        Assert.False(_settings.BiometricEnabled);
    }

    public void Dispose()
    {
        _session.Dispose();
        _database.CloseAsync().GetAwaiter().GetResult();
        _database.Dispose();
        File.Delete(_databasePath);
    }
}
