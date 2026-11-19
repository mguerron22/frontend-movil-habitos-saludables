using System.Globalization;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>Token de Firebase Messaging del teléfono (en Android: FirebaseMessaging.GetToken).</summary>
public interface IPushTokenSource
{
    /// <summary>null si el teléfono no tiene Google Play Services o Firebase no respondió.</summary>
    Task<string?> GetTokenAsync();
}

public sealed record PushTokenRequest(string DeviceId, string Token);

public sealed class PushApi(AuthenticatedApiClient api)
{
    public Task RegisterAsync(PushTokenRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, "api/push/devices", request, cancellationToken);
}

/// <summary>
/// Le dice al servidor a qué token avisar. Se repite tras cada sincronización, pero solo llama al servidor
/// si cambió el token o el usuario. Cerrar sesión no necesita darlo de baja: el servidor solo avisa a
/// dispositivos con sesión activa.
/// </summary>
public sealed class PushRegistration : IDisposable
{
    private readonly IPushTokenSource _source;
    private readonly PushApi _api;
    private readonly IAppSettings _settings;
    private readonly ISessionManager _session;
    private readonly ISyncService _sync;
    private readonly ILogger<PushRegistration> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PushRegistration(
        IPushTokenSource source,
        PushApi api,
        IAppSettings settings,
        ISessionManager session,
        ISyncService sync,
        ILogger<PushRegistration> logger)
    {
        _source = source;
        _api = api;
        _settings = settings;
        _session = session;
        _sync = sync;
        _logger = logger;
        _sync.Completed += OnSyncCompleted;
    }

    /// <param name="newToken">El que entregó Firebase al renovarlo; si no, se pide.</param>
    public async Task EnsureRegisteredAsync(string? newToken = null)
    {
        var user = _session.CurrentUser;
        if (user is null || _session.IsOfflineSession)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var token = newToken ?? await _source.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            var registration = Key(user.Id, token);
            if (_settings.PushRegistration == registration)
            {
                return;
            }

            await _api.RegisterAsync(new PushTokenRequest(_settings.DeviceId, token));
            _settings.PushRegistration = registration;
        }
        catch (Exception exception) when (exception is ApiException or ServerUnreachableException or SessionExpiredException)
        {
            // Se reintenta en la próxima sincronización; mientras tanto avisa la revisión periódica.
            _logger.LogInformation(exception, "No se pudo registrar el teléfono para avisos push.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _sync.Completed -= OnSyncCompleted;
        _gate.Dispose();
    }

    internal static string Key(Guid userId, string token) => $"{userId:N}|{token}";

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = EnsureRegisteredAsync();
}

/// <summary>
/// Llega un push de datos: arma la notificación (canal propio y versión privada en pantalla bloqueada) y
/// deja la recomendación como avisada para que la sincronización no la vuelva a mostrar.
/// </summary>
public sealed class PushMessageHandler(IAppSettings settings, IAppNotifications notifications, RecommendationBadge badge)
{
    public const string RecommendationType = "recommendation";

    /// <returns>true si conviene sincronizar ya para tener la recomendación guardada (y legible sin conexión).</returns>
    public bool Handle(IReadOnlyDictionary<string, string> data)
    {
        if (!data.TryGetValue("type", out var type) || type != RecommendationType)
        {
            return false;
        }

        var title = data.GetValueOrDefault("title") ?? "Nueva recomendación";
        var summary = data.GetValueOrDefault("summary") ?? "La IA analizó tus hábitos.";
        var createdAt = DateTimeOffset.TryParse(
            data.GetValueOrDefault("createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        if (createdAt is null || createdAt > settings.LastNotifiedRecommendationAt.GetValueOrDefault())
        {
            if (createdAt is not null)
            {
                settings.LastNotifiedRecommendationAt = createdAt;
            }

            notifications.ShowRecommendation(title, summary);
        }

        badge.MarkArrived(createdAt);
        return true;
    }
}

/// <summary>Punto en la campana del encabezado: hay una recomendación que el usuario todavía no abrió.</summary>
public sealed class RecommendationBadge : IDisposable
{
    private readonly Data.IRecommendationStore _store;
    private readonly IAppSettings _settings;
    private readonly ISyncService _sync;
    private readonly TimeProvider _timeProvider;

    public RecommendationBadge(Data.IRecommendationStore store, IAppSettings settings, ISyncService sync, TimeProvider timeProvider)
    {
        _store = store;
        _settings = settings;
        _sync = sync;
        _timeProvider = timeProvider;
        _sync.Completed += OnSyncCompleted;
    }

    public event EventHandler? Changed;

    public bool HasUnread { get; private set; }

    public async Task RefreshAsync()
    {
        var all = await _store.GetAllAsync();
        DateTimeOffset? newest = all.Count > 0 ? all[0].CreatedAt : null;
        var latest = Max(newest, _settings.LastNotifiedRecommendationAt);
        Set(latest is not null && latest > _settings.LastSeenRecommendationAt.GetValueOrDefault());
    }

    /// <summary>Llegó un push: se marca sin esperar a sincronizar.</summary>
    public void MarkArrived(DateTimeOffset? createdAt)
    {
        if (createdAt is null || createdAt > _settings.LastSeenRecommendationAt.GetValueOrDefault())
        {
            Set(true);
        }
    }

    /// <summary>El usuario abrió la pestaña IA.</summary>
    public void MarkSeen()
    {
        _settings.LastSeenRecommendationAt = _timeProvider.GetUtcNow();
        Set(false);
    }

    public void Dispose() => _sync.Completed -= OnSyncCompleted;

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    private void Set(bool value)
    {
        if (HasUnread == value)
        {
            return;
        }

        HasUnread = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = RefreshAsync();
}
