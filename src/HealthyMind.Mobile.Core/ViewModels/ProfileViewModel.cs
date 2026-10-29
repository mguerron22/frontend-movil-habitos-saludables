using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
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
        // Los cambios hechos sin conexión viven solo en el teléfono: avisar antes de que se pierdan.
        await sync.SyncAsync();
        var pending = await sync.PendingCountAsync();
        var warning = pending == 0
            ? string.Empty
            : $"\n\nOjo: tienes {pending} cambio(s) sin sincronizar que se perderán. Conéctate a internet antes de salir para guardarlos.";

        var confirmed = await dialogs.ConfirmAsync(
            "Cerrar sesión",
            "Se borrarán los datos guardados en este teléfono y tendrás que ingresar tu contraseña otra vez." + warning,
            "Cerrar sesión",
            "Cancelar");

        if (confirmed && await RunAsync(() => session.LogoutAsync()))
        {
            await navigation.GoToLoginAsync();
        }
    }
}
