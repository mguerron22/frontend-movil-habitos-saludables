namespace HealthyMind.Mobile.Core.Navigation;

/// <summary>Navegación entre pantallas; en MAUI la implementa Shell.</summary>
public interface INavigationService
{
    /// <param name="message">Aviso que se muestra arriba del login (p. ej. "Tu sesión expiró").</param>
    Task GoToLoginAsync(string? message = null);

    Task GoToRegisterAsync();

    /// <summary>Pantalla para escribir el código que llegó al correo.</summary>
    Task GoToVerifyEmailAsync(string email);

    Task GoToForgotPasswordAsync();

    /// <summary>Bienvenida y consentimiento de permisos (primer uso).</summary>
    Task GoToWelcomeAsync();

    Task GoToUnlockAsync();

    /// <summary>Entra a la app (pestañas) y descarta el historial de login.</summary>
    Task GoToMainAsync();

    /// <summary>Pestaña Hábitos.</summary>
    Task GoToHabitsAsync();

    /// <summary>Formulario de hábito: nuevo si <paramref name="habitId"/> es null.</summary>
    Task GoToHabitEditorAsync(Guid? habitId = null);

    Task GoToEditProfileAsync();

    Task GoToChangePasswordAsync();

    /// <summary>Dispositivos con la sesión abierta.</summary>
    Task GoToSessionsAsync();

    /// <summary>Administración de usuarios (solo Admin).</summary>
    Task GoToAdminUsersAsync();

    /// <summary>Tablero con las cifras de la plataforma (solo Admin).</summary>
    Task GoToAdminDashboardAsync();

    Task GoBackAsync();
}

/// <summary>Abre enlaces fuera de la app (navegador del sistema).</summary>
public interface IExternalLinks
{
    Task OpenAsync(Uri uri);
}

/// <summary>Diálogos del sistema.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message, string accept = "Entendido");

    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <returns>El texto ingresado, o null si canceló.</returns>
    Task<string?> PromptAsync(string title, string message, string? initialValue, string placeholder, PromptKind kind = PromptKind.Text);
}

/// <summary>Teclado que se muestra en un diálogo con campo de texto.</summary>
public enum PromptKind
{
    Text,
    Number,
    Url,
}

/// <summary>
/// Ejecuta en el hilo de la interfaz. La sincronización avisa desde hilos de fondo y en Android
/// tocar la interfaz desde ahí cierra la app.
/// </summary>
public interface IUiDispatcher
{
    Task InvokeAsync(Func<Task> action);
}
