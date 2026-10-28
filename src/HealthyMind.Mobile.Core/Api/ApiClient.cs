using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>
/// Envío y lectura de JSON contra el backend con un manejo de errores único:
/// errores HTTP → <see cref="ApiException"/>; sin red → <see cref="ServerUnreachableException"/>.
/// Las APIs por módulo (AuthApi, HabitsApi...) se apoyan en esta clase.
/// </summary>
/// <remarks>
/// La dirección del servidor se lee en cada llamada: se puede cambiar desde la app (celular físico) sin reiniciar.
/// </remarks>
public abstract class ApiClient(HttpClient httpClient, IConnectivityService connectivity, IAppSettings settings)
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(WithBody(HttpMethod.Get, path, null), cancellationToken);

    /// <summary>Para endpoints que responden 204 cuando no hay datos.</summary>
    public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        using var response = await SendRawAsync(WithBody(HttpMethod.Get, path, null), cancellationToken);
        return response.StatusCode == HttpStatusCode.NoContent
            ? null
            : await ReadAsync<T>(response, cancellationToken);
    }

    public Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(WithBody(HttpMethod.Post, path, body), cancellationToken);

    public Task<T> PutAsync<T>(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(WithBody(HttpMethod.Put, path, body), cancellationToken);

    public Task<T> PatchAsync<T>(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(WithBody(HttpMethod.Patch, path, body), cancellationToken);

    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(WithBody(method, path, body), cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            connectivity.ReportServerUnreachable();
            throw new ServerUnreachableException("No hay conexión con el servidor.", exception);
        }

        connectivity.ReportServerReachable();
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ProblemDetailsReader.ToExceptionAsync(response, cancellationToken);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new ApiException(response.StatusCode, "El servidor devolvió una respuesta vacía.");
    }

    private HttpRequestMessage WithBody(HttpMethod method, string path, object? body) =>
        new(method, Resolve(path)) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: JsonOptions) };

    private Uri Resolve(string path)
    {
        var baseUrl = settings.ApiBaseUrl.EndsWith('/') ? settings.ApiBaseUrl : settings.ApiBaseUrl + "/";
        return new Uri(new Uri(baseUrl), path.TrimStart('/'));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Para login, registro, refresh y logout: no lleva token.</summary>
public sealed class AnonymousApiClient(HttpClient httpClient, IConnectivityService connectivity, IAppSettings settings)
    : ApiClient(httpClient, connectivity, settings);

/// <summary>Para todo lo demás: agrega el token y lo renueva solo (ver <see cref="AuthenticatedHttpHandler"/>).</summary>
public sealed class AuthenticatedApiClient(HttpClient httpClient, IConnectivityService connectivity, IAppSettings settings)
    : ApiClient(httpClient, connectivity, settings);
