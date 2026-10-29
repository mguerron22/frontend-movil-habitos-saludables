using System.Net;
using System.Text;
using System.Text.Json;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Tests;

/// <summary>Servidor falso: responde según la ruta y cuenta las llamadas.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();
    private readonly Dictionary<string, int> _calls = new();
    private readonly object _gate = new();

    public List<string?> AuthorizationHeaders { get; } = [];

    public void On(string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _routes[path] = handler;

    public void On(string path, HttpStatusCode status, object? body = null) =>
        On(path, _ => Task.FromResult(Json(status, body)));

    public int Calls(string path)
    {
        lock (_gate)
        {
            return _calls.GetValueOrDefault(path);
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object? body) => new(status)
    {
        Content = body is null ? null : new StringContent(JsonSerializer.Serialize(body, ApiClient.JsonOptions), Encoding.UTF8, "application/json"),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        lock (_gate)
        {
            _calls[path] = _calls.GetValueOrDefault(path) + 1;
            AuthorizationHeaders.Add(request.Headers.Authorization?.Parameter);
        }

        return _routes.TryGetValue(path, out var handler)
            ? handler(request)
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class FakeSettings : IAppSettings
{
    public string ApiBaseUrl { get; set; } = "http://servidor.test";

    public string DeviceId => "device-test";

    public string DeviceName => "Pixel de pruebas";

    public bool BiometricEnabled { get; set; }

    public bool BiometricOffered { get; set; }

    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    public bool LocationEnabled { get; set; }

    public string? StepCounterState { get; set; }
}

internal sealed class FakeTokenStore : ISecureTokenStore
{
    public string? RefreshToken { get; set; }

    public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

    public Task SaveRefreshTokenAsync(string refreshToken)
    {
        RefreshToken = refreshToken;
        return Task.CompletedTask;
    }

    public void Clear() => RefreshToken = null;
}

internal sealed class FakeConnectivity : IConnectivityService
{
    public bool IsOnline { get; set; } = true;

    public bool ServerReachable { get; set; } = true;

    public event EventHandler<bool>? StatusChanged;

    public void ReportServerUnreachable() => IsOnline = false;

    public void ReportServerReachable() => IsOnline = true;

    public Task<bool> CheckNowAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServerReachable);

    public void Raise(bool online)
    {
        IsOnline = online;
        StatusChanged?.Invoke(this, online);
    }
}

internal static class Samples
{
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);

    public static UserProfile User(Guid? id = null) => new(
        id ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "ana.perez",
        "ana@test.com",
        "Ana",
        "Pérez",
        "User",
        "Active",
        "America/Guayaquil",
        Now,
        Now);

    public static AuthResponse Auth(string accessToken, string refreshToken, UserProfile? user = null, TimeSpan? accessLifetime = null) => new(
        accessToken,
        Now.Add(accessLifetime ?? TimeSpan.FromMinutes(15)),
        refreshToken,
        Now.AddDays(60),
        user ?? User());
}

/// <summary>Reloj fijo que se puede adelantar a mano.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
