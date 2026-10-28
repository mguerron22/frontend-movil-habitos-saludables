namespace HealthyMind.Mobile.Core.Auth;

/// <summary>Huella o rostro del sistema (Android: BiometricPrompt; Windows: Windows Hello).</summary>
public interface IBiometricAuthenticator
{
    /// <summary>true si el equipo tiene sensor y el usuario ya registró una huella o rostro.</summary>
    Task<bool> IsAvailableAsync();

    Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default);
}

public enum BiometricOutcome
{
    Success,
    Cancelled,
    Failed,
    NotAvailable,
}

public sealed record BiometricResult(BiometricOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome == BiometricOutcome.Success;

    public static BiometricResult Success() => new(BiometricOutcome.Success);
}
