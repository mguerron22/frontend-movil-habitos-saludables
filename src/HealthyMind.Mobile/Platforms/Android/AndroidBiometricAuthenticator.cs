using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using AndroidX.Lifecycle;
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

    public async Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default)
    {
        var activity = await WaitForResumedActivityAsync(cancellationToken);
        if (activity is null)
        {
            return cancellationToken.IsCancellationRequested
                ? new BiometricResult(BiometricOutcome.Cancelled)
                : new BiometricResult(BiometricOutcome.NotAvailable, "No se pudo abrir el lector de huella. Toca la huella otra vez.");
        }

        var completion = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BiometricPrompt? prompt = null;

        // Si Android descartó el diálogo nunca llama al callback: la cancelación termina la espera igual.
        using var registration = cancellationToken.Register(() =>
        {
            MainThread.BeginInvokeOnMainThread(() => prompt?.CancelAuthentication());
            completion.TrySetResult(new BiometricResult(BiometricOutcome.Cancelled));
        });

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (completion.Task.IsCompleted)
            {
                return;
            }

            var executor = ContextCompat.GetMainExecutor(activity)
                ?? throw new InvalidOperationException("Android no entregó el ejecutor del hilo principal.");
            prompt = new BiometricPrompt(activity, executor, new Callback(completion));
            var info = new BiometricPrompt.PromptInfo.Builder()
                .SetTitle(title)
                .SetSubtitle(subtitle)
                .SetNegativeButtonText("Cancelar")
                .SetAllowedAuthenticators(AllowedAuthenticators)
                .Build();
            prompt.Authenticate(info);
        });

        return await completion.Task;
    }

    /// <summary>
    /// Pedir la huella mientras la app todavía vuelve del fondo hace que Android descarte el diálogo sin avisar
    /// ("después de onSaveInstanceState"): se espera a que la pantalla esté de verdad al frente.
    /// </summary>
    private static async Task<FragmentActivity?> WaitForResumedActivityAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            var activity = await MainThread.InvokeOnMainThreadAsync(() =>
                Platform.CurrentActivity is FragmentActivity current
                && current.Lifecycle.CurrentState.IsAtLeast(Lifecycle.State.Resumed!)
                    ? current
                    : null);
            if (activity is not null)
            {
                return activity;
            }

            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
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
