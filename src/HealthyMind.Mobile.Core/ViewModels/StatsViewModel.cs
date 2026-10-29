using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Estadísticas (RF04, mock 3): consistencia global, la semana día por día, ritmo semanal,
/// distribución por dimensión y detalle por hábito. Se calcula en el teléfono: funciona sin conexión.
/// </summary>
public sealed partial class StatsViewModel : ViewModelBase, IDisposable
{
    private static readonly string[] DayLetters = ["L", "M", "X", "J", "V", "S", "D"];

    private readonly IHabitStore _store;
    private readonly ISessionManager _session;
    private readonly ISyncService _sync;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    private string _initials = string.Empty;

    /// <summary>true: el mes en curso; false: la semana (lunes a domingo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeek), nameof(PeriodLabel))]
    private bool _isMonth;

    [ObservableProperty]
    private string _consistencyPercent = "0%";

    [ObservableProperty]
    private string _consistencyDetail = string.Empty;

    [ObservableProperty]
    private string _trendText = string.Empty;

    [ObservableProperty]
    private bool _isTrendUp = true;

    [ObservableProperty]
    private bool _hasTrend;

    [ObservableProperty]
    private string _categoriesCount = "0";

    [ObservableProperty]
    private string _categoriesText = string.Empty;

    [ObservableProperty]
    private string _categoriesCaption = "focos";

    [ObservableProperty]
    private bool _hasData;

    public StatsViewModel(IHabitStore store, ISessionManager session, ISyncService sync, IUiDispatcher ui, TimeProvider timeProvider)
    {
        _store = store;
        _session = session;
        _sync = sync;
        _ui = ui;
        _timeProvider = timeProvider;
        _sync.Completed += OnSyncCompleted;
    }

    public bool IsWeek => !IsMonth;

    public string PeriodLabel => IsMonth ? "este mes" : "esta semana";

    /// <summary>Los 7 círculos L–D de la tarjeta de consistencia.</summary>
    public ObservableCollection<WeekDayStatus> WeekDays { get; } = [];

    /// <summary>Barras del "Ritmo semanal".</summary>
    public ObservableCollection<BarItem> WeekBars { get; } = [];

    public ObservableCollection<CategorySlice> Categories { get; } = [];

    public ObservableCollection<HabitStatsItem> HabitDetails { get; } = [];

    public async Task LoadAsync()
    {
        Initials = _session.CurrentUser?.Initials ?? string.Empty;
        await ReloadAsync();
    }

    public void Dispose() => _sync.Completed -= OnSyncCompleted;

    [RelayCommand]
    private Task ShowWeekAsync()
    {
        IsMonth = false;
        return ReloadAsync();
    }

    [RelayCommand]
    private Task ShowMonthAsync()
    {
        IsMonth = true;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var monday = StatisticsCalculator.StartOfWeek(today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var from = IsMonth ? monthStart : monday;
        var to = IsMonth ? monthStart.AddMonths(1).AddDays(-1) : monday.AddDays(6);
        var previousFrom = IsMonth ? monthStart.AddMonths(-1) : monday.AddDays(-7);

        var habits = await _store.GetHabitsAsync();
        var records = await _store.GetRecordsAsync(previousFrom < from ? previousFrom : from, to);
        var zone = TimeZoneInfo.Local;

        var period = StatisticsCalculator.Calculate(habits, records, from, to, today, zone);
        var week = IsMonth ? StatisticsCalculator.Calculate(habits, records, monday, monday.AddDays(6), today, zone) : period;
        var previous = StatisticsCalculator.Calculate(habits, records, previousFrom, from.AddDays(-1), today, zone);

        ConsistencyPercent = HabitFormat.Percent(period.Ratio);
        ConsistencyDetail = period.Expected == 0
            ? "Aún no hay hábitos previstos en este periodo."
            : $"Has completado {period.Completed} de {period.Expected} hábitos previstos {PeriodLabel}.";

        // Tendencia contra el periodo anterior, en puntos porcentuales (como el "+12% vs previa" del mock).
        HasTrend = previous.Expected > 0 && period.Expected > 0;
        var delta = (int)Math.Round((period.Ratio - previous.Ratio) * 100);
        IsTrendUp = delta >= 0;
        TrendText = $"{(delta >= 0 ? "+" : "")}{delta}% vs previa";

        FillWeek(week, monday, today);
        FillCategories(habits);
        FillHabits(period);
        HasData = habits.Count > 0;
    }

    private void FillWeek(PeriodStats week, DateOnly monday, DateOnly today)
    {
        WeekDays.Clear();
        WeekBars.Clear();
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            var stats = week.Days.FirstOrDefault(day => day.Date == date);
            var state = date > today
                ? DayState.Future
                : date == today
                    ? DayState.Today
                    : stats is { Expected: > 0 } && stats.Completed == stats.Expected
                        ? DayState.Done
                        : stats is { Completed: > 0 } ? DayState.Partial : DayState.Missed;

            WeekDays.Add(new WeekDayStatus(DayLetters[i], state));
            WeekBars.Add(new BarItem(DayLetters[i], stats?.Ratio ?? 0, date == today, date > today));
        }
    }

    private void FillCategories(IReadOnlyList<HabitRow> habits)
    {
        Categories.Clear();
        foreach (var share in StatisticsCalculator.Distribution(habits))
        {
            Categories.Add(new CategorySlice(CategoryInfo.For(share.Category), share.Share));
        }

        CategoriesCount = Categories.Count.ToString(HomeViewModel.Spanish);
        CategoriesText = HabitFormat.Count(Categories.Count, "categoría", "categorías");
        CategoriesCaption = Categories.Count == 1 ? "foco" : "focos";
    }

    private void FillHabits(PeriodStats period)
    {
        HabitDetails.Clear();
        foreach (var stats in period.Habits)
        {
            HabitDetails.Add(new HabitStatsItem(stats));
        }
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = _ui.InvokeAsync(ReloadAsync);
}

public enum DayState
{
    Done,
    Partial,
    Missed,
    Today,
    Future,
}

/// <summary>Círculo del día en la tarjeta de consistencia.</summary>
public sealed record WeekDayStatus(string Letter, DayState State)
{
    public bool IsDone => State == DayState.Done;

    public bool IsPartial => State == DayState.Partial;

    public bool IsToday => State == DayState.Today;

    public bool IsFuture => State == DayState.Future;

    public bool IsMissed => State == DayState.Missed;
}

/// <summary>Barra del ritmo semanal (0 a 1).</summary>
public sealed record BarItem(string Letter, double Value, bool IsToday, bool IsFuture)
{
    public string Percent => IsFuture ? string.Empty : HabitFormat.Percent(Value);
}

/// <summary>Porción de la dona por categoría.</summary>
public sealed record CategorySlice(CategoryInfo Info, double Share)
{
    public string Percent => HabitFormat.Percent(Share);

    public string Color => Info.ChartColor;
}

/// <summary>Fila de "Detalle por hábito".</summary>
public sealed class HabitStatsItem(HabitStats stats)
{
    public string Name => stats.Habit.Name;

    public CategoryInfo Category { get; } = CategoryInfo.For(stats.Habit.Category);

    public double Ratio => stats.Ratio;

    public string Percent => HabitFormat.Percent(stats.Ratio);

    public string Detail => stats.Expected == 0 ? "Sin días previstos" : $"{stats.Completed} de {HabitFormat.Count(stats.Expected, "día", "días")}";
}
