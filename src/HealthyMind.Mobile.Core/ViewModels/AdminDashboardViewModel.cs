using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Tablero del administrador: cuántos usan la plataforma y cómo (sin datos personales).</summary>
public sealed partial class AdminDashboardViewModel(
    AdminUsersApi adminApi,
    INavigationService navigation) : ViewModelBase
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private AdminDashboard? _dashboard;

    public bool HasData => Dashboard is not null;

    /// <summary>Cuentas: total, activas, suspendidas, por confirmar.</summary>
    public ObservableCollection<DashboardTile> AccountTiles { get; } = [];

    /// <summary>Uso: hábitos, registros, recomendaciones, teléfonos con avisos.</summary>
    public ObservableCollection<DashboardTile> UsageTiles { get; } = [];

    public ObservableCollection<DashboardBar> SignUps { get; } = [];

    public Task LoadAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        AdminDashboard? result = null;
        if (await RunAsync(async () => result = await adminApi.GetDashboardAsync()) && result is not null)
        {
            Show(result);
        }
    }

    [RelayCommand]
    private Task GoToUsersAsync() => navigation.GoToAdminUsersAsync();

    [RelayCommand]
    private Task GoBackAsync() => navigation.GoBackAsync();

    internal void Show(AdminDashboard data)
    {
        Dashboard = data;

        AccountTiles.Clear();
        AccountTiles.Add(new("Usuarios", Number(data.TotalUsers), $"{Number(data.NewUsersLast30Days)} nuevos en 30 días", DashboardTone.Primary));
        AccountTiles.Add(new("Activos", Number(data.ActiveUsers), $"{Number(data.UsersActiveLast7Days)} entraron esta semana", DashboardTone.Success));
        AccountTiles.Add(new("Suspendidos", Number(data.SuspendedUsers), "No pueden entrar", DashboardTone.Danger));
        AccountTiles.Add(new("Administradores", Number(data.Admins), $"{Number(data.PendingVerification)} correos sin confirmar", DashboardTone.Warning));

        UsageTiles.Clear();
        UsageTiles.Add(new("Hábitos activos", Number(data.ActiveHabits), "En todas las cuentas", DashboardTone.Primary));
        UsageTiles.Add(new("Registros", Number(data.RecordsLast7Days), "Últimos 7 días", DashboardTone.Success));
        UsageTiles.Add(new("Consejos IA", Number(data.RecommendationsLast7Days), Helpful(data.HelpfulPercentage), DashboardTone.Primary));
        UsageTiles.Add(new("Teléfonos", Number(data.PushDevices), "Con avisos push", DashboardTone.Warning));

        SignUps.Clear();
        var max = Math.Max(1, data.SignUpsLast14Days.Count == 0 ? 0 : data.SignUpsLast14Days.Max(day => day.Count));
        foreach (var day in data.SignUpsLast14Days)
        {
            SignUps.Add(new DashboardBar(
                day.Date.ToString("dd", Spanish),
                day.Count,
                Math.Max(4, 80.0 * day.Count / max)));
        }
    }

    private static string Number(int value) => value.ToString("N0", Spanish);

    private static string Helpful(double? percentage) =>
        percentage is null ? "Sin opiniones todavía" : $"{percentage.Value.ToString("0.#", Spanish)} % les sirvió";
}

public enum DashboardTone
{
    Primary,
    Success,
    Warning,
    Danger,
}

public sealed record DashboardTile(string Title, string Value, string Caption, DashboardTone Tone)
{
    public bool IsSuccess => Tone == DashboardTone.Success;

    public bool IsWarning => Tone == DashboardTone.Warning;

    public bool IsDanger => Tone == DashboardTone.Danger;
}

/// <param name="Height">Alto de la barra en unidades de pantalla (máximo 80).</param>
public sealed record DashboardBar(string Label, int Count, double Height);
