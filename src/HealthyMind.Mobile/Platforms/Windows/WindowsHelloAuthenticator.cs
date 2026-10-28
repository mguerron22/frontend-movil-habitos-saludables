using HealthyMind.Mobile.Core.Auth;
using Windows.Security.Credentials.UI;

namespace HealthyMind.Mobile.Platforms.Windows;

/// <summary>
/// En Windows (solo para desarrollar) la "huella" es Windows Hello: huella, rostro o PIN del equipo.
/// </summary>
internal sealed class WindowsHelloAuthenticator : IBiometricAuthenticator
{
    public async Task<bool> IsAvailableAsync() =>
        await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;

    public async Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default)
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is null)
        {
            return new BiometricResult(BiometricOutcome.NotAvailable, "No se pudo abrir Windows Hello.");
        }

        // Las apps de escritorio deben indicar la ventana dueña del diálogo de Windows Hello.
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(handle, $"{title}. {subtitle}");

        return result switch
        {
            UserConsentVerificationResult.Verified => BiometricResult.Success(),
            UserConsentVerificationResult.Canceled => new BiometricResult(BiometricOutcome.Cancelled),
            UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.NotConfiguredForUser
                or UserConsentVerificationResult.DisabledByPolicy => new BiometricResult(BiometricOutcome.NotAvailable, "Windows Hello no está configurado."),
            _ => new BiometricResult(BiometricOutcome.Failed, "No se pudo verificar tu identidad."),
        };
    }
}
