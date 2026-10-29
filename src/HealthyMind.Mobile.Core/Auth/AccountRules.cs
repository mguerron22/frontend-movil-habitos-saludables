using System.Net.Mail;

namespace HealthyMind.Mobile.Core.Auth;

/// <summary>Las reglas de cuenta del backend, para avisar en el formulario antes de enviar.</summary>
public static class AccountRules
{
    public const int PasswordMinLength = 8;

    /// <returns>El mensaje de error, o null si es válido.</returns>
    public static string? Email(string value)
    {
        var email = value.Trim();
        return email.Length > 0 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email
            ? null
            : "Escribe un correo válido.";
    }

    /// <returns>El mensaje de error, o null si es válida.</returns>
    public static string? Password(string value)
    {
        if (value.Length < PasswordMinLength)
        {
            return $"Mínimo {PasswordMinLength} caracteres.";
        }

        return value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit)
            ? null
            : "Usa al menos una mayúscula, una minúscula y un número.";
    }

    public static string? Required(string value, string message) =>
        string.IsNullOrWhiteSpace(value) ? message : null;
}
