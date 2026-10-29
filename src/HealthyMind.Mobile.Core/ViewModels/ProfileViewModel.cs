using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Perfil. Hecho: datos básicos, huella y cerrar sesión.
/// La mitad 2 agrega editar perfil, cambiar contraseña, dispositivos y la vista de administrador.
/// </summary>
public sealed partial class ProfileViewModel(
    ISessionManager session,
    BiometricEnrollment biometricEnrollment,
    IBiometricAuthenticator biometric,
    IAppSettings settings,
    ISyncService sync,
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
