namespace HealthyMind.Mobile.Core.Models;

// Contratos de /api/auth y /api/profile del backend. Los enums viajan como texto ("User", "Admin", "Active"...).

public sealed record LoginRequest(string UsernameOrEmail, string Password, string DeviceId, string? DeviceName);

public sealed record RegisterRequest(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    string DeviceId,
    string? DeviceName,
    string? TimeZoneId);

public sealed record RefreshRequest(string RefreshToken, string DeviceId);

public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfile User);

public sealed record UserProfile(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    string TimeZoneId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt)
{
    public const string AdminRole = "Admin";

    public bool IsAdmin => Role == AdminRole;

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>Iniciales para el avatar cuando no hay foto.</summary>
    public string Initials =>
        string.Concat(new[] { FirstName, LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part.Trim()[0])));
}
