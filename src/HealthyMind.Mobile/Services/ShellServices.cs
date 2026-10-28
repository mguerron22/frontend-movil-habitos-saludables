using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Services;

/// <summary>Rutas de Shell. Las partes 2 y 3 agregan aquí las de sus pantallas de detalle.</summary>
public static class Routes
{
    public const string Startup = "startup";
    public const string Login = "login";
    public const string Unlock = "unlock";
    public const string Main = "main";
    public const string Home = "home";
    public const string Register = "register";

    /// <summary>Parámetro de navegación con el aviso que muestra el login.</summary>
    public const string NoticeParameter = "notice";
}

internal sealed class ShellNavigationService : INavigationService
{
    public Task GoToLoginAsync(string? message = null) =>
        GoAsync($"//{Routes.Login}", new Dictionary<string, object> { [Routes.NoticeParameter] = message ?? string.Empty });

    public Task GoToRegisterAsync() => GoAsync(Routes.Register);

    public Task GoToUnlockAsync() => GoAsync($"//{Routes.Unlock}");

    public Task GoToMainAsync() => GoAsync($"//{Routes.Main}/{Routes.Home}");

    public Task GoBackAsync() => GoAsync("..");

    private static Task GoAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(() => parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, parameters));
}

internal sealed class ShellDialogService : IDialogService
{
    public Task AlertAsync(string title, string message, string accept = "Entendido") =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, accept));

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, accept, cancel));

    public Task<string?> PromptAsync(string title, string message, string? initialValue, string placeholder) =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayPromptAsync(
            title,
            message,
            "Guardar",
            "Cancelar",
            placeholder,
            maxLength: 200,
            keyboard: Keyboard.Url,
            initialValue: initialValue ?? string.Empty));

    private static Page CurrentPage => Shell.Current.CurrentPage ?? Shell.Current;
}
