using System.Net;
using System.Net.Http.Headers;
using HealthyMind.Mobile.Core.Auth;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>
/// Pone el access token en cada petición. Si el servidor responde 401, renueva una vez y reintenta;
/// así ninguna pantalla tiene que preocuparse por tokens vencidos.
/// </summary>
public sealed class AuthenticatedHttpHandler(IAccessTokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            // Se guarda el cuerpo en memoria para poder reenviarlo si hay que renovar el token.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await tokens.RenewAfterRejectionAsync(token, cancellationToken);
        using var retry = Clone(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Content = original.Content,
            Version = original.Version,
        };

        foreach (var header in original.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return copy;
    }
}
