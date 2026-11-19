using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Services;

/// <summary>Preferencias en el almacenamiento de la app (no cifrado: aquí no va nada sensible).</summary>
internal sealed class PreferencesAppSettings(IPreferences preferences) : IAppSettings
{
    private const string ApiBaseUrlKey = "api_base_url";
    private const string DeviceIdKey = "device_id";
    private const string BiometricEnabledKey = "biometric_enabled";
    private const string BiometricOfferedKey = "biometric_offered";
    private const string LastUsernameKey = "last_username";
    private const string RefreshExpiresKey = "refresh_expires_at";
    private const string LocationEnabledKey = "location_enabled";
    private const string StepCounterStateKey = "step_counter_state";
    private const string LastNotifiedRecommendationKey = "last_notified_recommendation_at";
    private const string PresentationModeKey = "presentation_mode";

    public string ApiBaseUrl
    {
        get => preferences.Get(ApiBaseUrlKey, DefaultApiBaseUrl());
        set => preferences.Set(ApiBaseUrlKey, value);
    }

    public string DeviceId
    {
        get
        {
            var id = preferences.Get(DeviceIdKey, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                preferences.Set(DeviceIdKey, id);
            }

            return id;
        }
    }

    public string DeviceName => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();

    public bool BiometricEnabled
    {
        get => preferences.Get(BiometricEnabledKey, false);
        set => preferences.Set(BiometricEnabledKey, value);
    }

    public bool BiometricOffered
    {
        get => preferences.Get(BiometricOfferedKey, false);
        set => preferences.Set(BiometricOfferedKey, value);
    }

    public string? LastUsername
    {
        get => preferences.Get<string?>(LastUsernameKey, null);
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                preferences.Remove(LastUsernameKey);
            }
            else
            {
                preferences.Set(LastUsernameKey, value);
            }
        }
    }

    public DateTimeOffset? RefreshTokenExpiresAt
    {
        get
        {
            var ticks = preferences.Get(RefreshExpiresKey, 0L);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => preferences.Set(RefreshExpiresKey, value?.UtcTicks ?? 0L);
    }

    public bool LocationEnabled
    {
        get => preferences.Get(LocationEnabledKey, false);
        set => preferences.Set(LocationEnabledKey, value);
    }

    public string? StepCounterState
    {
        get => preferences.Get<string?>(StepCounterStateKey, null);
        set => preferences.Set(StepCounterStateKey, value);
    }

    public DateTimeOffset? LastNotifiedRecommendationAt
    {
        get
        {
            var ticks = preferences.Get(LastNotifiedRecommendationKey, 0L);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => preferences.Set(LastNotifiedRecommendationKey, value?.UtcTicks ?? 0L);
    }

    public bool PresentationMode
    {
        get => preferences.Get(PresentationModeKey, false);
        set => preferences.Set(PresentationModeKey, value);
    }

    public string? PushRegistration
    {
        get => preferences.Get<string?>("push_registration", null);
        set => preferences.Set("push_registration", value);
    }

    public DateTimeOffset? LastSeenRecommendationAt
    {
        get
        {
            var ticks = preferences.Get("last_seen_recommendation", 0L);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => preferences.Set("last_seen_recommendation", value?.UtcTicks ?? 0L);
    }

    public bool ReminderEnabled
    {
        get => preferences.Get("reminder_enabled", false);
        set => preferences.Set("reminder_enabled", value);
    }

    public TimeOnly? ReminderTime
    {
        get
        {
            var minutes = preferences.Get("reminder_minutes", -1);
            return minutes < 0 ? null : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
        }
        set => preferences.Set("reminder_minutes", value is { } time ? (int)time.ToTimeSpan().TotalMinutes : -1);
    }

    public bool StreakAlertEnabled
    {
        get => preferences.Get("streak_alert_enabled", true);
        set => preferences.Set("streak_alert_enabled", value);
    }

    public bool AiPushEnabled
    {
        get => preferences.Get("ai_push_enabled", true);
        set => preferences.Set("ai_push_enabled", value);
    }

    public DateTimeOffset? SeenSyncPending
    {
        get
        {
            var ticks = preferences.Get("seen_sync_pending", 0L);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => preferences.Set("seen_sync_pending", value?.UtcTicks ?? 0L);
    }

    public bool BatteryPromptShown
    {
        get => preferences.Get("battery_prompt_shown", false);
        set => preferences.Set("battery_prompt_shown", value);
    }

    public int ConsentVersion
    {
        get => preferences.Get("consent_version", 0);
        set => preferences.Set("consent_version", value);
    }

    /// <summary>
    /// Release: el servidor de producción (HTTPS, detrás del IIS).
    /// Debug: el emulador de Android llega a la PC por 10.0.2.2; Windows, por localhost.
    /// En cualquier caso se puede cambiar desde el login (ícono de servidor).
    /// </summary>
    private static string DefaultApiBaseUrl()
    {
#if DEBUG
        return DeviceInfo.Current.Platform == DevicePlatform.Android ? "http://10.0.2.2:5080" : "http://localhost:5080";
#else
        return "https://maui.appservices.com.ec";
#endif
    }
}

/// <summary>Refresh token cifrado con SecureStorage (Android Keystore / DPAPI en Windows).</summary>
internal sealed class SecureTokenStore(ISecureStorage secureStorage) : ISecureTokenStore
{
    private const string RefreshTokenKey = "refresh_token";

    public Task<string?> GetRefreshTokenAsync() => secureStorage.GetAsync(RefreshTokenKey);

    public Task SaveRefreshTokenAsync(string refreshToken) => secureStorage.SetAsync(RefreshTokenKey, refreshToken);

    public void Clear() => secureStorage.Remove(RefreshTokenKey);
}

internal sealed class MauiNetworkStatus : INetworkStatus, IDisposable
{
    private readonly IConnectivity _connectivity;

    public MauiNetworkStatus(IConnectivity connectivity)
    {
        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnChanged;
    }

    public event EventHandler? Changed;

    public bool HasNetwork => _connectivity.NetworkAccess == NetworkAccess.Internet;

    public void Dispose() => _connectivity.ConnectivityChanged -= OnChanged;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>GPS con la API de MAUI (Android y Windows). Precisión media: basta para saber el lugar, no la calle exacta.</summary>
internal sealed class MauiLocationProvider(IGeolocation geolocation) : ILocationProvider
{
    public async Task<bool> EnsurePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>);
        }

        return status == PermissionStatus.Granted;
    }

    public async Task<GeoPoint?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
        {
            return null;
        }

        try
        {
            var location = await geolocation.GetLastKnownLocationAsync()
                           ?? await geolocation.GetLocationAsync(
                               new GeolocationRequest(GeolocationAccuracy.Medium, RecordLocation.Timeout), cancellationToken);
            return location is null ? null : new GeoPoint(Math.Round(location.Latitude, 5), Math.Round(location.Longitude, 5));
        }
        catch (Exception exception) when (exception is FeatureNotEnabledException or FeatureNotSupportedException or PermissionException)
        {
            // GPS apagado o no disponible: el registro se guarda sin ubicación.
            return null;
        }
    }
}

/// <summary>Plataformas sin contador de pasos (Windows): los hábitos de pasos se anotan a mano.</summary>
internal sealed class NoStepSensor : IStepSensor
{
    public bool IsSupported => false;

    public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);

    public Task<long?> ReadTotalAsync(CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
}

/// <summary>En Windows la app no avisa en segundo plano: el requisito de notificaciones es del celular.</summary>
internal sealed class NoAppNotifications : IAppNotifications
{
    public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);

    public void ShowRecommendation(string title, string text)
    {
    }

    public void ShowReminder(string reminderId, string title, string text, Guid? habitId = null)
    {
    }

    public void ShowAnnouncement(string title, string text)
    {
    }

    public void Dismiss(string reminderId)
    {
    }
}

/// <summary>En Windows no hay restricciones de batería que explicar.</summary>
internal sealed class NoBatteryOptimization : IBatteryOptimization
{
    public bool IsRestricted => false;

    public void RequestUnrestricted()
    {
    }
}

/// <summary>En Windows no hay Firebase.</summary>
internal sealed class NoPushTokenSource : IPushTokenSource
{
    public Task<string?> GetTokenAsync() => Task.FromResult<string?>(null);

    public Task SubscribeAsync(string topic) => Task.CompletedTask;
}

/// <summary>En Windows no hay recordatorios en segundo plano.</summary>
internal sealed class NoReminderSchedule : IReminderSchedule
{
    public void ScheduleDaily(string id, TimeOnly at)
    {
    }

    public void ScheduleOnce(string id, TimeSpan delay)
    {
    }

    public void Cancel(string id)
    {
    }
}

/// <summary>En Windows no hay avisos en segundo plano, así que el modo presentación no programa nada.</summary>
internal sealed class NoPresentationSchedule : IPresentationSchedule
{
    public bool IsPrecise => true;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void OpenPrecisionSettings()
    {
    }
}
