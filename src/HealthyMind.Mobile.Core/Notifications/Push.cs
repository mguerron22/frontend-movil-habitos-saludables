using System.Globalization;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>Firebase Messaging del teléfono (en Android: FirebaseMessaging).</summary>
public interface IPushTokenSource
{
    /// <summary>null si el teléfono no tiene Google Play Services o Firebase no respondió.</summary>
    Task<string?> GetTokenAsync();

    /// <summary>Recibir los mensajes de un tema (p. ej. los anuncios del administrador).</summary>
    Task SubscribeAsync(string topic);
}

/// <summary>
/// Restricción de batería del fabricante (TECNO, Xiaomi...): con ella Android atrasa o bloquea los avisos.
/// </summary>
public interface IBatteryOptimization
{
    bool IsRestricted { get; }

    /// <summary>Abre el diálogo del sistema para dejar a la app sin restricciones.</summary>
    void RequestUnrestricted();
}

public sealed record PushTokenRequest(string DeviceId, string Token, bool RecommendationsEnabled);

public sealed class PushApi(AuthenticatedApiClient api)
{
    public Task RegisterAsync(PushTokenRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, "api/push/devices", request, cancellationToken);

    public Task UnregisterAsync(string deviceId, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Delete, $"api/push/devices/{Uri.EscapeDataString(deviceId)}", cancellationToken: cancellationToken);
}

/// <summary>
/// Le dice al servidor a qué token avisar y si el usuario quiere los avisos de la IA. Se repite tras cada
/// sincronización, pero solo llama al servidor si cambió algo. Cerrar sesión no necesita darlo de baja
/// (el servidor solo avisa a sesiones activas); borrar los datos del teléfono sí.
/// </summary>
public sealed class PushRegistration : IDisposable
{
    public const string AnnouncementsTopic = "anuncios";

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

            var registration = Key(user.Id, token, _settings.AiPushEnabled);
            if (_settings.PushRegistration == registration)
            {
                return;
            }

            await _api.RegisterAsync(new PushTokenRequest(_settings.DeviceId, token, _settings.AiPushEnabled));
            await _source.SubscribeAsync(AnnouncementsTopic);
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

    /// <summary>El usuario cambió el interruptor "Avisos de la IA": se le informa al servidor.</summary>
    public Task SetAiNotificationsAsync(bool enabled)
    {
        _settings.AiPushEnabled = enabled;
        return EnsureRegisteredAsync();
    }

    /// <summary>Antes de borrar los datos del teléfono: que el servidor deje de avisar aquí.</summary>
    public async Task UnregisterAsync()
    {
        try
        {
            if (_session.CurrentUser is not null && !_session.IsOfflineSession)
            {
                await _api.UnregisterAsync(_settings.DeviceId);
            }
        }
        catch (Exception exception) when (exception is ApiException or ServerUnreachableException or SessionExpiredException)
        {
            // Sin conexión igual se borra: el servidor deja de avisar cuando la sesión se revoca o vence.
            _logger.LogInformation(exception, "No se pudo dar de baja el teléfono para avisos push.");
        }
        finally
        {
            _settings.PushRegistration = null;
        }
    }

    public void Dispose()
    {
        _sync.Completed -= OnSyncCompleted;
        _gate.Dispose();
    }

    internal static string Key(Guid userId, string token, bool aiEnabled) => $"{userId:N}|{(aiEnabled ? 1 : 0)}|{token}";

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = EnsureRegisteredAsync();
}

/// <summary>
/// Llega un push de datos: arma la notificación (canal propio y versión privada en pantalla bloqueada) y
/// deja la recomendación como avisada para que la sincronización no la vuelva a mostrar.
/// </summary>
public sealed class PushMessageHandler(IAppSettings settings, IAppNotifications notifications, RecommendationBadge badge)
{
    public const string RecommendationType = "recommendation";
    public const string AnnouncementType = "announcement";

    /// <returns>true si conviene sincronizar ya para tener la recomendación guardada (y legible sin conexión).</returns>
    public bool Handle(IReadOnlyDictionary<string, string> data)
    {
        var type = data.GetValueOrDefault("type");
        if (type == AnnouncementType)
        {
            notifications.ShowAnnouncement(data.GetValueOrDefault("title") ?? "HealthyMindIA", data.GetValueOrDefault("body") ?? string.Empty);
            return false;
        }

        if (type != RecommendationType)
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

            if (settings.AiPushEnabled)
            {
                notifications.ShowRecommendation(title, summary);
            }
        }

        badge.MarkArrived(createdAt);
        return true;
    }
}

/// <summary>Punto en la campana del encabezado: hay una recomendación que el usuario todavía no abrió.</summary>
public sealed class RecommendationBadge : IDisposable
{
    private readonly IRecommendationStore _store;
    private readonly IAppSettings _settings;
    private readonly ISyncService _sync;
    private readonly TimeProvider _timeProvider;

    public RecommendationBadge(IRecommendationStore store, IAppSettings settings, ISyncService sync, TimeProvider timeProvider)
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

    /// <summary>El usuario abrió la pestaña IA. Queda pendiente avisarlo al servidor (para sus otros teléfonos).</summary>
    public void MarkSeen()
    {
        var now = _timeProvider.GetUtcNow();
        _settings.LastSeenRecommendationAt = now;
        _settings.SeenSyncPending = now;
        Set(false);
    }

    public void Dispose() => _sync.Completed -= OnSyncCompleted;

    internal static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
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

/// <summary>
/// Lo que el usuario dice de las recomendaciones ("Me sirvió", visto). Se guarda en el teléfono y se envía
/// cuando hay conexión: funciona igual sin internet.
/// </summary>
public sealed class RecommendationFeedback : IDisposable
{
    private readonly IRecommendationStore _store;
    private readonly RecommendationApi _api;
    private readonly IAppSettings _settings;
    private readonly ISessionManager _session;
    private readonly ISyncService _sync;
    private readonly ILogger<RecommendationFeedback> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecommendationFeedback(
        IRecommendationStore store,
        RecommendationApi api,
        IAppSettings settings,
        ISessionManager session,
        ISyncService sync,
        ILogger<RecommendationFeedback> logger)
    {
        _store = store;
        _api = api;
        _settings = settings;
        _session = session;
        _sync = sync;
        _logger = logger;
        _sync.Completed += OnSyncCompleted;
    }

    public async Task RateAsync(Guid recommendationId, bool helpful)
    {
        await _store.SetFeedbackAsync(recommendationId, helpful);
        await SendPendingAsync();
    }

    public async Task SendPendingAsync()
    {
        if (_session.CurrentUser is null || _session.IsOfflineSession)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            foreach (var row in await _store.GetPendingFeedbackAsync())
            {
                await _api.RateAsync(row.Id, row.WasHelpful == true);
                await _store.MarkFeedbackSentAsync(row.Id);
            }

            if (_settings.SeenSyncPending is { } seenAt)
            {
                await _api.MarkSeenAsync(seenAt);
                if (_settings.SeenSyncPending == seenAt)
                {
                    _settings.SeenSyncPending = null;
                }
            }
        }
        catch (Exception exception) when (exception is ApiException or ServerUnreachableException or SessionExpiredException)
        {
            // Queda pendiente: se reintenta en la próxima sincronización.
            _logger.LogInformation(exception, "No se pudo enviar la opinión sobre las recomendaciones.");
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

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = SendPendingAsync();
}
