using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>Pide a la IA una recomendación al momento. Solo responde si el servidor lo permite (en desarrollo).</summary>
public sealed class RecommendationApi(AuthenticatedApiClient api)
{
    public Task RequestNowAsync(CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, "api/recommendations/now", cancellationToken: cancellationToken);
}

/// <summary>El despertador del sistema que hace correr una revisión cada pocos minutos, aun con la app cerrada.</summary>
public interface IPresentationSchedule
{
    /// <summary>false si el sistema puede atrasar el aviso porque falta el permiso de alarmas exactas.</summary>
    bool IsPrecise { get; }

    void Start();

    void Stop();

    /// <summary>Abre los ajustes del teléfono donde se permiten las alarmas exactas.</summary>
    void OpenPrecisionSettings();
}

/// <summary>
/// Modo presentación: en vez de esperar la recomendación diaria, la IA genera una nueva cada
/// <see cref="Interval"/> y llega como notificación. Sirve para mostrar los avisos en una demostración.
/// </summary>
public sealed class PresentationMode(IAppSettings settings, IPresentationSchedule schedule)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    public bool IsEnabled => settings.PresentationMode;

    public bool IsPrecise => schedule.IsPrecise;

    public void Enable()
    {
        settings.PresentationMode = true;
        schedule.Start();
    }

    public void Disable()
    {
        settings.PresentationMode = false;
        schedule.Stop();
    }

    /// <summary>Al abrir la app vuelve a programar el despertador (el sistema lo borra al reiniciar el teléfono).</summary>
    public void Restore()
    {
        if (IsEnabled)
        {
            schedule.Start();
        }
    }

    public void OpenPrecisionSettings() => schedule.OpenPrecisionSettings();
}
