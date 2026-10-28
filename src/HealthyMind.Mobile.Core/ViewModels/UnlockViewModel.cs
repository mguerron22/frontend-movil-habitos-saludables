using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Entrada con huella. La huella libera la sesión guardada; si no hay internet se entra igual
/// con los datos del teléfono y se sincroniza al volver la conexión.
/// </summary>
public sealed partial class UnlockViewModel(
    ISessionManager session,
    IBiometricAuthenticator biometric,
    IUserCache userCache,
    INavigationService navigation) : ViewModelBase
{
    // Evita dos diálogos de huella seguidos (la pantalla puede "aparecer" dos veces al navegar).
    private bool _prompting;

    [ObservableProperty]
    private string _greeting = "¡Hola de nuevo!";

    [ObservableProperty]
    private string _initials = string.Empty;

    public async Task LoadAsync()
    {
        var user = await userCache.GetAsync();
        if (user is not null)
        {
            Greeting = $"¡Hola de nuevo, {user.FirstName}!";
            Initials = user.Initials;
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy || _prompting)
        {
            return;
        }

        ErrorMessage = null;
        BiometricResult result;
        _prompting = true;
        try
        {
            result = await biometric.AuthenticateAsync("Desbloquear HealthyMindIA", "Usa tu huella para entrar");
        }
        finally
        {
            _prompting = false;
        }

        if (!result.Succeeded)
        {
            ErrorMessage = result.Outcome == BiometricOutcome.Cancelled
                ? null
                : result.Message ?? "No se reconoció la huella. Inténtalo otra vez.";
            return;
        }

        ResumeOutcome outcome = ResumeOutcome.NoSession;
        if (await RunAsync(async () => outcome = await session.ResumeAsync()))
        {
            await SessionNavigator.ContinueAsync(outcome, navigation);
        }
    }

    /// <summary>Entrar con contraseña. Conserva los datos locales por si es el mismo usuario.</summary>
    [RelayCommand]
    private Task UsePasswordAsync() => navigation.GoToLoginAsync();
}
