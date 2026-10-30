using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>Avisos del sistema (en Android, la barra de notificaciones).</summary>
public interface IAppNotifications
{
    /// <summary>Pide permiso para mostrar avisos (Android 13+). Si el usuario lo niega no se insiste.</summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Aviso de recomendación nueva; al tocarlo se abre la pestaña IA.</summary>
    void ShowRecommendation(string title, string text);
}

/// <summary>
/// Avisa cuando la sincronización trae una recomendación nueva de la IA. Mira cada sincronización
/// (con la app abierta) y la revisión periódica en segundo plano (con la app cerrada).
/// </summary>
public sealed class RecommendationNotifier : IDisposable
{
    /// <summary>
    /// Sin aviso previo (app recién instalada o primer uso) solo cuenta lo de las últimas 24 h,
    /// para no avisar de recomendaciones viejas al entrar en un teléfono nuevo.
    /// </summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(24);

    private readonly IRecommendationStore _store;
    private readonly IAppSettings _settings;
    private readonly IAppNotifications _notifications;
    private readonly ISyncService _sync;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecommendationNotifier(
        IRecommendationStore store,
        IAppSettings settings,
        IAppNotifications notifications,
        ISyncService sync,
        TimeProvider timeProvider)
    {
        _store = store;
        _settings = settings;
        _notifications = notifications;
        _sync = sync;
        _timeProvider = timeProvider;
        _sync.Completed += OnSyncCompleted;
    }

    /// <summary>Muestra la recomendación más reciente si todavía no se avisó. Una sola, aunque hayan llegado varias.</summary>
    public async Task CheckAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var all = await _store.GetAllAsync();
            var newest = all.Count > 0 ? all[0] : null;
            var alreadyNotified = _settings.LastNotifiedRecommendationAt ?? _timeProvider.GetUtcNow() - FreshFor;
            if (newest is null || newest.CreatedAt <= alreadyNotified)
            {
                return;
            }

            _settings.LastNotifiedRecommendationAt = newest.CreatedAt;
            _notifications.ShowRecommendation(newest.Title, newest.Summary);
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

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = CheckAsync();
}

/// <summary>
/// Lo que hace la app cuando el sistema la despierta en segundo plano: retoma la sesión guardada
/// (sin pedir huella: no se muestra nada de la app), sincroniza y avisa si hay recomendación nueva.
/// </summary>
public sealed class BackgroundCheck(
    ISessionManager session,
    ISyncService sync,
    RecommendationNotifier notifier,
    RecommendationApi recommendations)
{
    /// <param name="askForNew">Modo presentación: antes de sincronizar, pide a la IA una recomendación nueva.</param>
    /// <param name="cancellationToken">Cancelación: el sistema cortó la revisión.</param>
    public async Task RunAsync(bool askForNew = false, CancellationToken cancellationToken = default)
    {
        // Con la app abierta la sesión ya está viva; si no, se renueva con el refresh token guardado.
        if (session.CurrentUser is null && await session.ResumeAsync(cancellationToken) != ResumeOutcome.Online)
        {
            return;
        }

        if (askForNew)
        {
            await RequestNewAsync(cancellationToken);
        }

        await sync.SyncAsync(cancellationToken);
        await notifier.CheckAsync();
    }

    private async Task RequestNewAsync(CancellationToken cancellationToken)
    {
        try
        {
            await recommendations.RequestNowAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or ServerUnreachableException)
        {
            // Servidor que no lo permite (producción) o IA caída: se sincroniza igual con lo que haya.
        }
    }
}
