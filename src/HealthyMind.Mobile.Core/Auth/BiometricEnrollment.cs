using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Auth;

/// <summary>Después del primer ingreso con contraseña ofrece entrar con huella las próximas veces.</summary>
public sealed class BiometricEnrollment(IBiometricAuthenticator biometric, IAppSettings settings, IDialogService dialogs)
{
    public async Task OfferIfNeededAsync()
    {
        if (settings.BiometricOffered || settings.BiometricEnabled || !await biometric.IsAvailableAsync())
        {
            return;
        }

        settings.BiometricOffered = true;
        var accepted = await dialogs.ConfirmAsync(
            "Entra con tu huella",
            "La próxima vez no tendrás que escribir tu contraseña: solo tu huella. ¿La activamos?",
            "Activar",
            "Ahora no");

        if (accepted)
        {
            await EnableAsync();
        }
    }

    /// <summary>Pide la huella una vez para confirmar que es el dueño antes de activarla.</summary>
    public async Task<bool> EnableAsync()
    {
        var result = await biometric.AuthenticateAsync("Activar huella", "Confirma con tu huella para activarla");
        settings.BiometricEnabled = result.Succeeded;
        return result.Succeeded;
    }

    public void Disable() => settings.BiometricEnabled = false;
}
