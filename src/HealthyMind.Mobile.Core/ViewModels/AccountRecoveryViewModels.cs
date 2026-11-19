using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Formato del código que llega por correo.</summary>
public static class EmailCode
{
    public const int Length = 6;

    public static string Clean(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static bool IsValid(string? value) => Clean(value).Length == Length;
}

/// <summary>
/// Confirmar el correo con el código de 6 dígitos. Se llega aquí al registrarse
/// o al intentar entrar con una cuenta que aún no lo confirmó.
/// </summary>
public sealed partial class VerifyEmailViewModel(
    ISessionManager session,
    AuthApi authApi,
    BiometricEnrollment biometricEnrollment,
    INavigationService navigation,
    TimeProvider timeProvider) : ViewModelBase
{
    private static readonly TimeSpan ResendWait = TimeSpan.FromSeconds(60);

    private DateTimeOffset _lastSentAt = timeProvider.GetUtcNow();

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? _info;

    public bool HasInfo => !string.IsNullOrEmpty(Info);

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (!EmailCode.IsValid(Code))
        {
            ErrorMessage = "Escribe los 6 dígitos que llegaron a tu correo.";
            return;
        }

        if (!await RunAsync(() => session.VerifyEmailAsync(Email, EmailCode.Clean(Code))))
        {
            return;
        }

        Code = string.Empty;
        await biometricEnrollment.OfferIfNeededAsync();
        await navigation.GoToMainAsync();
    }

    [RelayCommand]
    private async Task ResendAsync()
    {
        var wait = _lastSentAt + ResendWait - timeProvider.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            Info = $"Espera {Math.Ceiling(wait.TotalSeconds):0} segundos para pedir otro código.";
            return;
        }

        if (await RunAsync(() => authApi.ResendVerificationAsync(new EmailRequest(Email))))
        {
            _lastSentAt = timeProvider.GetUtcNow();
            Info = "Te enviamos un código nuevo. Revisa también la carpeta de spam.";
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => navigation.GoToLoginAsync();
}

/// <summary>"Olvidé mi contraseña": primero pide el correo, luego el código y la contraseña nueva.</summary>
public sealed partial class ForgotPasswordViewModel(
    AuthApi authApi,
    IAppSettings settings,
    INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    private string _email = settings.LastUsername is { } last && last.Contains('@') ? last : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingEmail))]
    private bool _codeSent;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    [ObservableProperty]
    private string? _emailError;

    [ObservableProperty]
    private string? _codeError;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private string? _confirmPasswordError;

    public bool IsAskingEmail => !CodeSent;

    [RelayCommand]
    private async Task SendCodeAsync()
    {
        EmailError = AccountRules.Email(Email);
        if (EmailError is not null)
        {
            return;
        }

        if (await RunAsync(() => authApi.ForgotPasswordAsync(new EmailRequest(Email.Trim()))))
        {
            CodeSent = true;
        }
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        CodeError = EmailCode.IsValid(Code) ? null : "Escribe los 6 dígitos que llegaron a tu correo.";
        PasswordError = AccountRules.Password(NewPassword);
        ConfirmPasswordError = NewPassword == ConfirmPassword ? null : "Las contraseñas no coinciden.";
        if (CodeError is not null || PasswordError is not null || ConfirmPasswordError is not null)
        {
            return;
        }

        var ok = await RunAsync(() => authApi.ResetPasswordAsync(
            new ResetPasswordRequest(Email.Trim(), EmailCode.Clean(Code), NewPassword)));
        if (!ok)
        {
            return;
        }

        NewPassword = ConfirmPassword = Code = string.Empty;
        settings.LastUsername = Email.Trim();
        await navigation.GoToLoginAsync("Listo, tu contraseña cambió. Inicia sesión con la nueva.");
    }

    /// <summary>Volver a escribir el correo (p. ej. se equivocó).</summary>
    [RelayCommand]
    private void ChangeEmail()
    {
        CodeSent = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task GoBackAsync() => navigation.GoBackAsync();

    protected override void OnApiError(ApiException exception)
    {
        base.OnApiError(exception);
        PasswordError = exception.FirstErrorFor("newPassword") ?? PasswordError;
        CodeError = exception.FirstErrorFor("code") ?? CodeError;
    }
}
