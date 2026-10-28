using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using HealthyMind.Mobile.Core.Auth;
using Java.Lang;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>Huella con BiometricPrompt de AndroidX (solo biometría fuerte: huella o rostro de clase 3).</summary>
internal sealed class AndroidBiometricAuthenticator : IBiometricAuthenticator
{
    private const int AllowedAuthenticators = BiometricManager.Authenticators.BiometricStrong;

    public Task<bool> IsAvailableAsync()
    {
        var manager = BiometricManager.From(Platform.AppContext);
        return Task.FromResult(manager.CanAuthenticate(AllowedAuthenticators) == BiometricManager.BiometricSuccess);
    }

    public Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default)
    {
        if (Platform.CurrentActivity is not FragmentActivity activity)
        {
            return Task.FromResult(new BiometricResult(BiometricOutcome.NotAvailable, "No se pudo abrir el lector de huella."));
        }

        var completion = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var executor = ContextCompat.GetMainExecutor(activity)
                ?? throw new InvalidOperationException("Android no entregó el ejecutor del hilo principal.");
            var prompt = new BiometricPrompt(activity, executor, new Callback(completion));
            var info = new BiometricPrompt.PromptInfo.Builder()
                .SetTitle(title)
                .SetSubtitle(subtitle)
                .SetNegativeButtonText("Cancelar")
                .SetAllowedAuthenticators(AllowedAuthenticators)
                .Build();

            cancellationToken.Register(prompt.CancelAuthentication);
            prompt.Authenticate(info);
        });

        return completion.Task;
    }

    private sealed class Callback(TaskCompletionSource<BiometricResult> completion) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) =>
            completion.TrySetResult(BiometricResult.Success());

        public override void OnAuthenticationError(int errorCode, ICharSequence errString)
        {
            var cancelled = errorCode is BiometricPrompt.ErrorNegativeButton
                or BiometricPrompt.ErrorUserCanceled
                or BiometricPrompt.ErrorCanceled;

            completion.TrySetResult(cancelled
                ? new BiometricResult(BiometricOutcome.Cancelled)
                : new BiometricResult(BiometricOutcome.Failed, errString.ToString()));
        }

        // Un intento con un dedo no reconocido: el diálogo sigue abierto y deja reintentar, no se cierra la tarea.
        public override void OnAuthenticationFailed()
        {
        }
    }
}
