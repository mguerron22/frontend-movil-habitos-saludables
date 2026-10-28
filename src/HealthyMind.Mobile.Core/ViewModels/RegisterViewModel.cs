using System.Net.Mail;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Registro con las mismas reglas que el backend, para avisar antes de enviar.
/// Si el servidor rechaza algo (usuario o correo tomados), el error se pinta en el campo correspondiente.
/// </summary>
public sealed partial class RegisterViewModel(
    ISessionManager session,
    BiometricEnrollment biometricEnrollment,
    INavigationService navigation) : ViewModelBase
{
    public const int PasswordMinLength = 8;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    [ObservableProperty]
    private string? _firstNameError;

    [ObservableProperty]
    private string? _lastNameError;

    [ObservableProperty]
    private string? _usernameError;

    [ObservableProperty]
    private string? _emailError;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private string? _confirmPasswordError;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!Validate())
        {
            ErrorMessage = "Revisa los campos marcados.";
            return;
        }

        var ok = await RunAsync(() => session.RegisterAsync(new NewAccount(Username, Email, FirstName, LastName, Password)));
        if (!ok)
        {
            return;
        }

        Password = ConfirmPassword = string.Empty;
        await biometricEnrollment.OfferIfNeededAsync();
        await navigation.GoToMainAsync();
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task GoBackAsync() => navigation.GoBackAsync();

    internal bool Validate()
    {
        FirstNameError = Required(FirstName, "Escribe tu nombre.");
        LastNameError = Required(LastName, "Escribe tu apellido.");
        UsernameError = ValidateUsername(Username);
        EmailError = ValidateEmail(Email);
        PasswordError = ValidatePassword(Password);
        ConfirmPasswordError = Password == ConfirmPassword ? null : "Las contraseñas no coinciden.";

        return FirstNameError is null && LastNameError is null && UsernameError is null
               && EmailError is null && PasswordError is null && ConfirmPasswordError is null;
    }

    protected override void OnApiError(ApiException exception)
    {
        base.OnApiError(exception);

        UsernameError = exception.FirstErrorFor("username") ?? UsernameError;
        EmailError = exception.FirstErrorFor("email") ?? EmailError;
        PasswordError = exception.FirstErrorFor("password") ?? PasswordError;

        // Un 409 trae el motivo en el mensaje general: se lleva al campo al que corresponde.
        if (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            if (exception.Message.Contains("correo", StringComparison.OrdinalIgnoreCase))
            {
                EmailError = exception.Message;
            }
            else
            {
                UsernameError = exception.Message;
            }
        }
    }

    private static string? Required(string value, string message) =>
        string.IsNullOrWhiteSpace(value) ? message : null;

    private static string? ValidateUsername(string value)
    {
        var username = value.Trim();
        if (username.Length is < 4 or > 50)
        {
            return "El usuario debe tener entre 4 y 50 caracteres.";
        }

        return UsernamePattern().IsMatch(username)
            ? null
            : "Solo letras, números, punto, guion y guion bajo.";
    }

    private static string? ValidateEmail(string value)
    {
        var email = value.Trim();
        return email.Length > 0 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email
            ? null
            : "Escribe un correo válido.";
    }

    private static string? ValidatePassword(string value)
    {
        if (value.Length < PasswordMinLength)
        {
            return $"Mínimo {PasswordMinLength} caracteres.";
        }

        return value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit)
            ? null
            : "Usa al menos una mayúscula, una minúscula y un número.";
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex UsernamePattern();
}
