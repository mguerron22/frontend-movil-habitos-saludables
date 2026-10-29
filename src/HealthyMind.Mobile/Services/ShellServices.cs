using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Services;

/// <summary>Rutas de Shell. Cada pantalla de detalle nueva agrega aquí la suya.</summary>
public static class Routes
{
    public const string Startup = "startup";
    public const string Login = "login";
    public const string Unlock = "unlock";
    public const string Main = "main";
    public const string Home = "home";
    public const string Register = "register";
    public const string Habits = "habits";
    public const string HabitEditor = "habit-editor";

    /// <summary>Parámetro con el id del hábito a editar.</summary>
    public const string HabitIdParameter = "id";

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

    public Task GoToHabitsAsync() => GoAsync($"//{Routes.Main}/{Routes.Habits}");

    public Task GoToHabitEditorAsync(Guid? habitId = null) => habitId is null
        ? GoAsync(Routes.HabitEditor)
        : GoAsync(Routes.HabitEditor, new Dictionary<string, object> { [Routes.HabitIdParameter] = habitId.Value });

    public Task GoBackAsync() => GoAsync("..");

    private static Task GoAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(() => parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, parameters));
}

internal sealed class MauiUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);
}

internal sealed class ShellDialogService : IDialogService
{
    public Task AlertAsync(string title, string message, string accept = "Entendido") =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, accept));

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, accept, cancel));

    public Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue,
        string placeholder,
        PromptKind kind = PromptKind.Text) =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayPromptAsync(
            title,
            message,
            "Guardar",
            "Cancelar",
            placeholder,
            maxLength: 200,
            keyboard: kind switch
            {
                PromptKind.Number => Keyboard.Numeric,
                PromptKind.Url => Keyboard.Url,
                _ => Keyboard.Text,
            },
            initialValue: initialValue ?? string.Empty));

    private static Page CurrentPage => Shell.Current.CurrentPage ?? Shell.Current;
}
