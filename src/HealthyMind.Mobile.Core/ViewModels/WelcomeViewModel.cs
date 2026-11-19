using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Bienvenida del primer uso (RNF02): explica qué datos usa la app y para qué, pide cada permiso
/// con su motivo y registra la aceptación de la política de privacidad.
/// Ningún permiso es obligatorio; solo aceptar la política lo es.
/// </summary>
public sealed partial class WelcomeViewModel(
    IAppNotifications notifications,
    IStepSensor steps,
    RecordLocation location,
    IBatteryOptimization battery,
    IAppSettings settings,
    IExternalLinks links,
    StartupViewModel startup) : ViewModelBase
{
    /// <summary>Subirla cuando cambie la política: todos vuelven a ver la bienvenida.</summary>
    public const int CurrentConsentVersion = 1;

    public const string PrivacyPath = "privacidad.html";

    [ObservableProperty]
    private bool _notificationsGranted;

    [ObservableProperty]
    private bool _stepsGranted;

    [ObservableProperty]
    private bool _locationGranted = settings.LocationEnabled;

    [ObservableProperty]
    private bool _batteryUnrestricted = !battery.IsRestricted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _acceptedPolicy;

    public bool StepsSupported => steps.IsSupported;

    public static bool IsPending(IAppSettings settings) => settings.ConsentVersion < CurrentConsentVersion;

    public Uri PrivacyUri => new(new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/"), PrivacyPath);

    [RelayCommand]
    private async Task AllowNotificationsAsync() => NotificationsGranted = await notifications.EnsurePermissionAsync();

    [RelayCommand]
    private async Task AllowStepsAsync() => StepsGranted = steps.IsSupported && await steps.EnsurePermissionAsync();

    /// <summary>Activa la ubicación en los registros; se puede apagar después en Perfil.</summary>
    [RelayCommand]
    private async Task AllowLocationAsync() => LocationGranted = await location.SetEnabledAsync(true);

    [RelayCommand]
    private void AllowBattery()
    {
        if (battery.IsRestricted)
        {
            battery.RequestUnrestricted();
        }

        settings.BatteryPromptShown = true;
        BatteryUnrestricted = !battery.IsRestricted;
    }

    /// <summary>Pide uno tras otro los permisos (la ubicación queda aparte: es opcional y se activa a propósito).</summary>
    [RelayCommand]
    private async Task AllowAllAsync()
    {
        await AllowNotificationsAsync();
        if (StepsSupported)
        {
            await AllowStepsAsync();
        }

        AllowBattery();
    }

    [RelayCommand]
    private Task OpenPrivacyAsync() => links.OpenAsync(PrivacyUri);

    /// <summary>Al volver de Ajustes (batería) se refresca lo que se ve.</summary>
    public void Refresh() => BatteryUnrestricted = !battery.IsRestricted;

    [RelayCommand(CanExecute = nameof(AcceptedPolicy))]
    private async Task ContinueAsync()
    {
        settings.ConsentVersion = CurrentConsentVersion;
        await startup.ProceedAsync();
    }
}
