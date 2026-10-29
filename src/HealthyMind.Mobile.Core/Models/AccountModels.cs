namespace HealthyMind.Mobile.Core.Models;

// Contratos de /api/profile, /api/auth/sessions y /api/admin/users del backend.

public sealed record UpdateProfileRequest(string FirstName, string LastName, string Email, string? TimeZoneId);

/// <param name="DeviceId">La sesión de este teléfono se conserva; las demás se cierran.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string DeviceId);

/// <summary>Un dispositivo con la sesión abierta.</summary>
public sealed record SessionResponse(Guid Id, string DeviceId, string? DeviceName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public static class UserStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
}
