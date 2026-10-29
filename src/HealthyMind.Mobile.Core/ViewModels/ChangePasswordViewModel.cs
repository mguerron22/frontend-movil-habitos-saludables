using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Cambiar la contraseña. El servidor cierra la sesión en los demás dispositivos y conserva la de este.</summary>
public sealed partial class ChangePasswordViewModel(
    ProfileApi profileApi,
    IAppSettings settings,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    [ObservableProperty]
    private string? _currentPasswordError;

    [ObservableProperty]
    private string? _newPasswordError;

    [ObservableProperty]
    private string? _confirmPasswordError;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate())
        {
            ErrorMessage = "Revisa los campos marcados.";
            return;
        }

        var ok = await RunAsync(() => profileApi.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword, settings.DeviceId)));
        if (!ok)
        {
            return;
        }

        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        await dialogs.AlertAsync(
            "Contraseña cambiada",
            "Listo. Por seguridad se cerró tu sesión en los demás dispositivos; en este teléfono sigues dentro.");
        await navigation.GoBackAsync();
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task CancelAsync() => navigation.GoBackAsync();

    internal bool Validate()
    {
        CurrentPasswordError = string.IsNullOrEmpty(CurrentPassword) ? "Escribe tu contraseña actual." : null;
        NewPasswordError = AccountRules.Password(NewPassword)
            ?? (NewPassword == CurrentPassword ? "Debe ser distinta de la actual." : null);
        ConfirmPasswordError = NewPassword == ConfirmPassword ? null : "Las contraseñas no coinciden.";
        return CurrentPasswordError is null && NewPasswordError is null && ConfirmPasswordError is null;
    }

    protected override void OnApiError(ApiException exception)
    {
        base.OnApiError(exception);
        NewPasswordError = exception.FirstErrorFor("newPassword") ?? NewPasswordError;
        CurrentPasswordError = exception.FirstErrorFor("currentPassword")
            ?? (exception.Message.Contains("actual", StringComparison.OrdinalIgnoreCase) ? exception.Message : CurrentPasswordError);
    }
}
