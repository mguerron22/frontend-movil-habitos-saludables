using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Habits;

public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>GPS del teléfono.</summary>
public interface ILocationProvider
{
    /// <summary>Pide el permiso de ubicación si hace falta.</summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Ubicación actual, o null si no hay permiso, GPS o respuesta a tiempo.</summary>
    Task<GeoPoint?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Ubicación opcional de cada registro (RF03): solo si el usuario la activó en su perfil.
/// Nunca bloquea el registro: sin GPS se guarda igual, sin ubicación.
/// </summary>
public sealed class RecordLocation(ILocationProvider provider, IAppSettings settings)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public bool IsEnabled => settings.LocationEnabled;

    /// <returns>true si quedó activada (el usuario dio el permiso).</returns>
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        settings.LocationEnabled = enabled && await provider.EnsurePermissionAsync();
        return settings.LocationEnabled;
    }

    public async Task<GeoPoint?> CurrentIfEnabledAsync()
    {
        if (!settings.LocationEnabled)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            return await provider.GetCurrentAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
