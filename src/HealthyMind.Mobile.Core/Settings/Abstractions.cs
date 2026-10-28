namespace HealthyMind.Mobile.Core.Settings;

/// <summary>Preferencias no sensibles de la app (en MAUI: Preferences).</summary>
public interface IAppSettings
{
    /// <summary>Dirección del backend. En el emulador Android es http://10.0.2.2:5080; en un celular, la IP de la PC.</summary>
    string ApiBaseUrl { get; set; }

    /// <summary>Identificador estable de esta instalación; el backend ata la sesión a él.</summary>
    string DeviceId { get; }

    string DeviceName { get; }

    /// <summary>El usuario aceptó entrar con huella.</summary>
    bool BiometricEnabled { get; set; }

    /// <summary>Ya se le preguntó si quiere usar huella (para no insistir en cada login).</summary>
    bool BiometricOffered { get; set; }

    /// <summary>Hasta cuándo sirve el refresh token guardado, para no intentar reanudar una sesión vencida.</summary>
    DateTimeOffset? RefreshTokenExpiresAt { get; set; }
}

/// <summary>Almacenamiento cifrado del refresh token (en MAUI: SecureStorage, respaldado por Android Keystore).</summary>
public interface ISecureTokenStore
{
    Task<string?> GetRefreshTokenAsync();

    Task SaveRefreshTokenAsync(string refreshToken);

    void Clear();
}
