using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>Endpoints de /api/auth. No usan token, por eso van por el cliente anónimo.</summary>
public sealed class AuthApi(AnonymousApiClient api)
{
    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<AuthResponse>("api/auth/login", request, cancellationToken);

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<AuthResponse>("api/auth/register", request, cancellationToken);

    public Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<AuthResponse>("api/auth/refresh", request, cancellationToken);

    public Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, "api/auth/logout", request, cancellationToken);
}

/// <summary>Ping a /health; cualquier respuesta 2xx cuenta como servidor disponible.</summary>
public sealed class HealthServerProbe(IHttpClientFactory httpClientFactory, Settings.IAppSettings settings) : IServerProbe
{
    public const string HttpClientName = "health";

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(new Uri(new Uri(settings.ApiBaseUrl), "health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }
}
