namespace HealthyMind.Mobile.Services;

/// <summary>
/// La app se abrió tocando una notificación. Si la sesión ya está abierta se va directo a la pantalla;
/// si no, se espera a que el usuario entre (contraseña o huella) y se va después: el aviso no salta el acceso.
/// </summary>
public static class NotificationLaunch
{
    /// <summary>Extra del Intent de la notificación con la pantalla a abrir.</summary>
    public const string Extra = "healthymind.open";

    public const string Insights = "insights";

    private static string? _pending;

    public static void Request(string? screen)
    {
        if (screen != Insights)
        {
            return;
        }

        if (IsInsideApp())
        {
            _ = OpenInsightsAsync();
            return;
        }

        _pending = screen;
    }

    /// <summary>Al entrar a la app, abre la pantalla que pidió la notificación (si hubo una).</summary>
    public static Task OpenPendingAsync()
    {
        if (Interlocked.Exchange(ref _pending, null) is null)
        {
            return Task.CompletedTask;
        }

        return OpenInsightsAsync();
    }

    private static bool IsInsideApp() =>
        Shell.Current?.CurrentState.Location.OriginalString.StartsWith($"//{Routes.Main}", StringComparison.Ordinal) == true;

    private static Task OpenInsightsAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync($"//{Routes.Main}/{Routes.Insights}"));
}
