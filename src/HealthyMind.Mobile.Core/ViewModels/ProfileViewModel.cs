using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Perfil (RF01): datos, huella, accesos a editar perfil, contraseña y dispositivos, administración y cerrar sesión.</summary>
public sealed partial class ProfileViewModel(
    ISessionManager session,
    BiometricEnrollment biometricEnrollment,
    IBiometricAuthenticator biometric,
    IAppSettings settings,
    ISyncService sync,
    RecordLocation location,
    PresentationMode presentation,
    IAppNotifications notifications,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    private bool _loading;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isBiometricAvailable;

    [ObservableProperty]
    private bool _isBiometricEnabled;

    [ObservableProperty]
    private bool _isLocationEnabled;

    [ObservableProperty]
    private bool _isPresentationEnabled;

    public async Task LoadAsync()
    {
        _loading = true;
        var user = session.CurrentUser;
        FullName = user?.FullName ?? string.Empty;
        Email = user?.Email ?? string.Empty;
        Username = user is null ? string.Empty : "@" + user.Username;
        Initials = user?.Initials ?? string.Empty;
        IsAdmin = user?.IsAdmin == true;
        IsBiometricAvailable = await biometric.IsAvailableAsync();
        IsBiometricEnabled = settings.BiometricEnabled;
        IsLocationEnabled = location.IsEnabled;
        IsPresentationEnabled = presentation.IsEnabled;
        _loading = false;
    }

    /// <summary>El interruptor de huella: activarla exige confirmar con la huella.</summary>
    async partial void OnIsBiometricEnabledChanged(bool value)
    {
        if (_loading || value == settings.BiometricEnabled)
        {
            return;
        }

        if (!value)
        {
            biometricEnrollment.Disable();
            return;
        }

        if (!await biometricEnrollment.EnableAsync())
        {
            _loading = true;
            IsBiometricEnabled = false;
            _loading = false;
        }
    }

    /// <summary>Guardar la ubicación de cada registro (RF03): activarla pide el permiso de ubicación.</summary>
    async partial void OnIsLocationEnabledChanged(bool value)
    {
        if (_loading || value == location.IsEnabled)
        {
            return;
        }

        if (await location.SetEnabledAsync(value) != value)
        {
            _loading = true;
            IsLocationEnabled = false;
            _loading = false;
            await dialogs.AlertAsync(
                "Sin permiso de ubicación",
                "Para guardar dónde registras tus hábitos, permite la ubicación a HealthyMindIA en los ajustes del teléfono.");
        }
    }

    /// <summary>Modo presentación: la IA escribe cada 3 minutos para mostrar las notificaciones.</summary>
    async partial void OnIsPresentationEnabledChanged(bool value)
    {
        if (_loading || value == presentation.IsEnabled)
        {
            return;
        }

        if (!value)
        {
            presentation.Disable();
            return;
        }

        if (!await notifications.EnsurePermissionAsync())
        {
            _loading = true;
            IsPresentationEnabled = false;
            _loading = false;
            await dialogs.AlertAsync(
                "Sin permiso de notificaciones",
                "Permite las notificaciones a HealthyMindIA en los ajustes del teléfono para ver los avisos de la IA.");
            return;
        }

        presentation.Enable();
        if (!presentation.IsPrecise && await dialogs.ConfirmAsync(
                "Avisos a tiempo",
                "Para que el aviso llegue justo cada 3 minutos, activa \"Alarmas y recordatorios\" para HealthyMindIA. Sin eso, Android puede atrasarlo.",
                "Abrir ajustes",
                "Ahora no"))
        {
            presentation.OpenPrecisionSettings();
        }
    }

    [RelayCommand]
    private Task EditProfileAsync() => navigation.GoToEditProfileAsync();

    [RelayCommand]
    private Task ChangePasswordAsync() => navigation.GoToChangePasswordAsync();

    [RelayCommand]
    private Task SessionsAsync() => navigation.GoToSessionsAsync();

    [RelayCommand]
    private Task AdminUsersAsync() => navigation.GoToAdminUsersAsync();

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // Los datos se quedan en el teléfono; solo se pierde lo pendiente si después entra otra persona.
        var pending = await PendingAfterSyncAsync();
        var warning = pending == 0
            ? string.Empty
            : $"\n\nTienes {pending} cambio(s) sin sincronizar: se enviarán cuando vuelvas a entrar. Si entra otra persona en este teléfono, se perderán.";

        var confirmed = await dialogs.ConfirmAsync(
            "Cerrar sesión",
            "Tendrás que ingresar tu contraseña para volver a entrar. Tus datos se quedan en este teléfono." + warning,
            "Cerrar sesión",
            "Cancelar");

        if (confirmed && await RunAsync(() => session.LogoutAsync()))
        {
            await navigation.GoToLoginAsync();
        }
    }

    /// <summary>Para prestar o vender el teléfono: cierra la sesión y borra todo lo guardado.</summary>
    [RelayCommand]
    private async Task ClearDeviceDataAsync()
    {
        var pending = await PendingAfterSyncAsync();
        var warning = pending == 0
            ? string.Empty
            : $"\n\nOjo: tienes {pending} cambio(s) sin sincronizar que se perderán. Conéctate a internet antes para guardarlos.";

        var confirmed = await dialogs.ConfirmAsync(
            "Borrar datos de este teléfono",
            "Se cerrará la sesión y se borrarán tus hábitos, registros y preferencias guardados aquí. "
            + "Tu cuenta y lo ya sincronizado siguen en el servidor." + warning,
            "Borrar",
            "Cancelar");

        if (confirmed && await RunAsync(() => session.ClearDeviceDataAsync()))
        {
            await navigation.GoToLoginAsync();
        }
    }

    private async Task<int> PendingAfterSyncAsync()
    {
        await sync.SyncAsync();
        return await sync.PendingCountAsync();
    }
}
