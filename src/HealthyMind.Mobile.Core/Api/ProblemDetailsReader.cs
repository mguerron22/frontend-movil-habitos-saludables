using System.Net;
using System.Text.Json;

namespace HealthyMind.Mobile.Core.Api;

/// <summary>Convierte las respuestas de error del backend (RFC 9457) en un mensaje para mostrar.</summary>
internal static class ProblemDetailsReader
{
    public static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            body = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ApiException(status, DefaultMessage(status));
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errors = ReadErrors(root);
            var detail = ReadString(root, "detail");
            var title = ReadString(root, "title");

            // En un 400 con errores por campo, el primero es más útil que "Revisa los campos marcados".
            var message = errors.Count > 0 && status == HttpStatusCode.BadRequest
                ? errors.Values.First()[0]
                : detail ?? title ?? DefaultMessage(status);

            return new ApiException(status, message, errors);
        }
        catch (JsonException)
        {
            return new ApiException(status, DefaultMessage(status));
        }
    }

    private static Dictionary<string, string[]> ReadErrors(JsonElement root)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("errors", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return errors;
        }

        foreach (var field in element.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Array)
            {
                errors[field.Name] = field.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToArray();
            }
        }

        return errors;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Tu sesión no es válida. Inicia sesión otra vez.",
        HttpStatusCode.Forbidden => "No tienes permiso para hacer esto.",
        HttpStatusCode.NotFound => "No se encontró lo que buscas.",
        HttpStatusCode.TooManyRequests => "Demasiados intentos. Espera un minuto.",
        >= HttpStatusCode.InternalServerError => "El servidor tuvo un problema. Intenta más tarde.",
        _ => "No se pudo completar la operación.",
    };
}
