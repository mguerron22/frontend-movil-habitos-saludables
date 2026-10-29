using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>Perfil propio y dispositivos con sesión abierta (RF01).</summary>
public sealed class ProfileApi(AuthenticatedApiClient api)
{
    public Task<UserProfile> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        api.PutAsync<UserProfile>("api/profile", request, cancellationToken);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, "api/profile/password", request, cancellationToken);

    public Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<IReadOnlyList<SessionResponse>>("api/auth/sessions", cancellationToken);

    public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Delete, $"api/auth/sessions/{sessionId}", cancellationToken: cancellationToken);
}

/// <summary>Administración de cuentas: solo para el rol Admin.</summary>
public sealed class AdminUsersApi(AuthenticatedApiClient api)
{
    public const int PageSize = 20;

    /// <param name="status">null para todos; si no, <see cref="UserStatuses"/>.</param>
    public Task<PagedResult<UserProfile>> SearchAsync(string? search, string? status, int page, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        }

        if (status is not null)
        {
            query.Add("status=" + status);
        }

        return api.GetAsync<PagedResult<UserProfile>>("api/admin/users?" + string.Join('&', query), cancellationToken);
    }

    public Task<UserProfile> SuspendAsync(Guid userId, CancellationToken cancellationToken = default) =>
        api.PatchAsync<UserProfile>($"api/admin/users/{userId}/suspend", null, cancellationToken);

    public Task<UserProfile> ReactivateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        api.PatchAsync<UserProfile>($"api/admin/users/{userId}/reactivate", null, cancellationToken);
}
