using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HealthyMind.Mobile.Core.Auth;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Pantalla de inicio. Parte 1: saludo y fecha. La parte 2 agrega la tarjeta de energía del día y los hábitos de hoy.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly ISessionManager session;
    private readonly TimeProvider timeProvider;

    public HomeViewModel(ISessionManager session, TimeProvider timeProvider)
    {
        this.session = session;
        this.timeProvider = timeProvider;
        session.OnlineRestored += OnOnlineRestored;
    }

    public static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    [ObservableProperty]
    private string _greeting = string.Empty;

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _isOfflineSession;

    public Task LoadAsync()
    {
        var now = timeProvider.GetLocalNow();
        var user = session.CurrentUser;

        Greeting = $"{GreetingFor(now.Hour)}, {user?.FirstName ?? "bienvenido"}!";
        TodayText = Capitalize(now.ToString("dddd, d 'de' MMMM", Spanish));
        Initials = user?.Initials ?? string.Empty;
        IsOfflineSession = session.IsOfflineSession;
        return Task.CompletedTask;
    }

    public void Dispose() => session.OnlineRestored -= OnOnlineRestored;

    private void OnOnlineRestored(object? sender, EventArgs e) => IsOfflineSession = false;

    internal static string GreetingFor(int hour) => hour switch
    {
        < 12 => "¡Buenos días",
        < 19 => "¡Buenas tardes",
        _ => "¡Buenas noches",
    };

    internal static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Spanish) + text[1..];
}
