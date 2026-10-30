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
    /// <summary>Diálogo de huella en curso; un toque nuevo lo cancela y abre otro.</summary>
    private CancellationTokenSource? _prompt;

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

    /// <summary>
    /// Pide la huella. Si Android descartó el diálogo sin avisar (pasa al volver del fondo), tocar otra vez
    /// cancela ese intento y abre uno nuevo: la huella nunca queda trabada hasta reiniciar la app.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UnlockAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var previous = _prompt;
        using var prompt = new CancellationTokenSource();
        _prompt = prompt;
        previous?.Cancel();
        ErrorMessage = null;

        var result = await biometric.AuthenticateAsync("Desbloquear HealthyMindIA", "Usa tu huella para entrar", prompt.Token);
        if (_prompt != prompt)
        {
            return; // Lo reemplazó un toque más nuevo.
        }

        _prompt = null;

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
