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

/// <summary>La cuenta se creó sin sesión: falta confirmar el correo con el código enviado.</summary>
public sealed record RegisterResponse(string Email, int CodeExpiresInMinutes);

public sealed record VerifyEmailRequest(string Email, string Code, string DeviceId, string? DeviceName);

public sealed record EmailRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

public sealed record ChangeRoleRequest(string Role);

public sealed record DailyCount(DateOnly Date, int Count);

public sealed record AdminDashboard(
    int TotalUsers,
    int ActiveUsers,
    int SuspendedUsers,
    int Admins,
    int PendingVerification,
    int NewUsersLast30Days,
    int UsersActiveLast7Days,
    int ActiveHabits,
    int RecordsLast7Days,
    int RecommendationsLast7Days,
    double? HelpfulPercentage,
    int PushDevices,
    IReadOnlyList<DailyCount> SignUpsLast14Days);

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
    DateTimeOffset? LastLoginAt,
    bool EmailVerified = true)
{
    public const string AdminRole = "Admin";

    public const string UserRole = "User";

    public bool IsAdmin => Role == AdminRole;

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>Iniciales para el avatar cuando no hay foto.</summary>
    public string Initials =>
        string.Concat(new[] { FirstName, LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part.Trim()[0])));
}
