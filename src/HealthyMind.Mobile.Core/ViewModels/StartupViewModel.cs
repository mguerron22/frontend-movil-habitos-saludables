using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Decide la primera pantalla: login (sin sesión), huella (sesión guardada y huella activada)
/// o directo a la app (sesión guardada sin huella).
/// </summary>
public sealed class StartupViewModel(
    ISessionManager session,
    IBiometricAuthenticator biometric,
    IAppSettings settings,
    INavigationService navigation) : ViewModelBase
{
    public async Task StartAsync()
    {
        if (!await session.HasStoredSessionAsync())
        {
            await navigation.GoToLoginAsync();
            return;
        }

        if (settings.BiometricEnabled && await biometric.IsAvailableAsync())
        {
            await navigation.GoToUnlockAsync();
            return;
        }

        await SessionNavigator.ContinueAsync(await session.ResumeAsync(), navigation);
    }
}

/// <summary>Qué pantalla sigue según cómo terminó el intento de retomar la sesión.</summary>
internal static class SessionNavigator
{
    public static Task ContinueAsync(ResumeOutcome outcome, INavigationService navigation) => outcome switch
    {
        ResumeOutcome.Online or ResumeOutcome.Offline => navigation.GoToMainAsync(),
        ResumeOutcome.Expired => navigation.GoToLoginAsync(SessionManager.ExpiredMessage),
        ResumeOutcome.Suspended => navigation.GoToLoginAsync(SessionManager.SuspendedMessage),
        ResumeOutcome.Unavailable => navigation.GoToLoginAsync(
            "No hay conexión y este teléfono no tiene datos guardados. Conéctate para entrar la primera vez."),
        _ => navigation.GoToLoginAsync(),
    };
}
