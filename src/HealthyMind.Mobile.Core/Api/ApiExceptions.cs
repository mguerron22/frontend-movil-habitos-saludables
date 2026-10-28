using System.Net;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>
/// El servidor respondió con error. <see cref="Exception.Message"/> trae el texto en español del backend (ProblemDetails).
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(message)
    {
        StatusCode = statusCode;
        FieldErrors = fieldErrors ?? new Dictionary<string, string[]>();
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Errores por campo de un 400 (las claves vienen en camelCase, p. ej. "password").</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;

    public string? FirstErrorFor(string field) =>
        FieldErrors.FirstOrDefault(pair => string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
}

/// <summary>No se llegó al servidor (sin red, servidor caído o tiempo agotado). La app debe pasar a modo sin conexión.</summary>
public sealed class ServerUnreachableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>La sesión ya no sirve (token vencido, revocado o cuenta suspendida): hay que volver al login.</summary>
public sealed class SessionExpiredException(SessionEndReason reason, string message) : Exception(message)
{
    public SessionEndReason Reason { get; } = reason;
}

public enum SessionEndReason
{
    Expired,
    Suspended,
}
