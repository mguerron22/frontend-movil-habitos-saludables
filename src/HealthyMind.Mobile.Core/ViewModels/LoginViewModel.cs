using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

public sealed partial class LoginViewModel(
    ISessionManager session,
    BiometricEnrollment biometricEnrollment,
    IAppSettings settings,
    IConnectivityService connectivity,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    /// <summary>Viene lleno con el último usuario que entró en este teléfono.</summary>
    [ObservableProperty]
    private string _usernameOrEmail = settings.LastUsername ?? string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    /// <summary>Aviso que llega de otra pantalla (sesión expirada, cuenta suspendida...).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public string ServerUrl => settings.ApiBaseUrl;

    /// <summary>El servidor respondió que falta confirmar el correo: se va a escribir el código.</summary>
    private string? _pendingVerification;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(UsernameOrEmail) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Escribe tu usuario o correo y tu contraseña.";
            return;
        }

        _pendingVerification = null;
        var ok = await RunAsync(() => session.LoginAsync(UsernameOrEmail, Password));
        if (!ok)
        {
            if (_pendingVerification is { } email)
            {
                Password = string.Empty;
                await navigation.GoToVerifyEmailAsync(email);
            }

            return;
        }

        Password = string.Empty;
        Notice = null;
        await biometricEnrollment.OfferIfNeededAsync();
        await navigation.GoToMainAsync();
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task GoToRegisterAsync() => navigation.GoToRegisterAsync();

    [RelayCommand]
    private Task ForgotPasswordAsync() => navigation.GoToForgotPasswordAsync();

    protected override void OnApiError(ApiException exception)
    {
        base.OnApiError(exception);
        if (exception.IsEmailNotVerified)
        {
            _pendingVerification = exception.Email ?? UsernameOrEmail.Trim();
        }
    }

    /// <summary>Para probar en un celular físico: la dirección del backend es la IP de la PC, no localhost.</summary>
    [RelayCommand]
    private async Task ConfigureServerAsync()
    {
        var value = await dialogs.PromptAsync(
            "Servidor",
            "Dirección del backend. En un celular usa la IP de tu PC, por ejemplo http://192.168.1.20:5080",
            settings.ApiBaseUrl,
            "http://192.168.1.20:5080",
            PromptKind.Url);

        if (value is null)
        {
            return;
        }

        if (!ServerAddress.TryNormalize(value, out var normalized))
        {
            await dialogs.AlertAsync("Servidor", "La dirección no es válida. Debe empezar con http:// o https://.");
            return;
        }

        settings.ApiBaseUrl = normalized;
        OnPropertyChanged(nameof(ServerUrl));

        var online = await connectivity.CheckNowAsync();
        await dialogs.AlertAsync(
            "Servidor",
            online ? "Conexión correcta con el servidor." : "Se guardó, pero el servidor no responde en esa dirección.");
    }
}

public static class ServerAddress
{
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = value.Trim().TrimEnd('/');
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host);
    }
}
