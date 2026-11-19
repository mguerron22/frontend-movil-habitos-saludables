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

    /// <summary>Usuario o correo del último ingreso: el login aparece ya lleno y solo falta la contraseña.</summary>
    string? LastUsername { get; set; }

    /// <summary>Hasta cuándo sirve el refresh token guardado, para no intentar reanudar una sesión vencida.</summary>
    DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    /// <summary>El usuario aceptó guardar dónde registra sus hábitos (RF03, opcional).</summary>
    bool LocationEnabled { get; set; }

    /// <summary>Última lectura del contador de pasos (ver <see cref="Habits.StepDay"/>).</summary>
    string? StepCounterState { get; set; }

    /// <summary>Fecha de la última recomendación de la IA de la que se avisó, para no repetir el aviso.</summary>
    DateTimeOffset? LastNotifiedRecommendationAt { get; set; }

    /// <summary>Modo presentación encendido: la IA escribe cada pocos minutos (ver <see cref="Notifications.PresentationMode"/>).</summary>
    bool PresentationMode { get; set; }

    /// <summary>"usuario|token" ya registrado en el servidor para avisos push (ver <see cref="Notifications.PushRegistration"/>).</summary>
    string? PushRegistration { get; set; }

    /// <summary>Cuándo abrió el usuario la pestaña IA por última vez: apaga el punto de la campana.</summary>
    DateTimeOffset? LastSeenRecommendationAt { get; set; }

    /// <summary>Recordatorio diario de hábitos pendientes encendido.</summary>
    bool ReminderEnabled { get; set; }

    /// <summary>Hora local del recordatorio; null = la de por defecto (20:00).</summary>
    TimeOnly? ReminderTime { get; set; }

    /// <summary>Aviso a las 21:00 si una racha se pierde hoy. Encendido por defecto.</summary>
    bool StreakAlertEnabled { get; set; }

    /// <summary>Avisos push de la IA en este teléfono (se le informa al servidor). Encendido por defecto.</summary>
    bool AiPushEnabled { get; set; }

    /// <summary>"Visto" de la pestaña IA que falta avisar al servidor (se envía al volver la conexión).</summary>
    DateTimeOffset? SeenSyncPending { get; set; }

    /// <summary>Ya se explicó cómo quitar la restricción de batería: no se insiste.</summary>
    bool BatteryPromptShown { get; set; }

    /// <summary>Versión de la política de privacidad aceptada (0 = nunca). Si sube, se vuelve a mostrar la bienvenida.</summary>
    int ConsentVersion { get; set; }
}

/// <summary>Almacenamiento cifrado del refresh token (en MAUI: SecureStorage, respaldado por Android Keystore).</summary>
public interface ISecureTokenStore
{
    Task<string?> GetRefreshTokenAsync();

    Task SaveRefreshTokenAsync(string refreshToken);

    void Clear();
}
