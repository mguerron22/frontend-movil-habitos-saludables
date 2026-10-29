using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// "Tu Brújula Diaria" (RF05, mock 4). Las recomendaciones las genera el backend solo con IA y llegan por la
/// sincronización; se leen del teléfono, así que también se ven sin conexión.
/// </summary>
public sealed partial class InsightsViewModel : ViewModelBase, IDisposable
{
    /// <summary>Días de historial para encontrar el mejor y el peor día de la semana.</summary>
    public const int PatternDays = 28;

    private readonly IRecommendationStore _recommendations;
    private readonly IHabitStore _habits;
    private readonly ISessionManager _session;
    private readonly ISyncService _sync;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _hasRecommendation;

    [ObservableProperty]
    private string _generatedText = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string? _focusHabit;

    [ObservableProperty]
    private string _modelName = string.Empty;

    [ObservableProperty]
    private bool _hasPatterns;

    [ObservableProperty]
    private string _bestDayValue = string.Empty;

    [ObservableProperty]
    private string _bestDayLabel = string.Empty;

    [ObservableProperty]
    private string _worstDayValue = string.Empty;

    [ObservableProperty]
    private string _worstDayLabel = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    public InsightsViewModel(
        IRecommendationStore recommendations,
        IHabitStore habits,
        ISessionManager session,
        ISyncService sync,
        IUiDispatcher ui,
        TimeProvider timeProvider)
    {
        _recommendations = recommendations;
        _habits = habits;
        _session = session;
        _sync = sync;
        _ui = ui;
        _timeProvider = timeProvider;
        _sync.Completed += OnSyncCompleted;
    }

    public bool HasFocusHabit => !string.IsNullOrWhiteSpace(FocusHabit);

    /// <summary>Acciones de la recomendación, una por línea (la IA las manda con "• ").</summary>
    public ObservableCollection<string> Actions { get; } = [];

    public ObservableCollection<PastRecommendation> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    public async Task LoadAsync()
    {
        Initials = _session.CurrentUser?.Initials ?? string.Empty;
        await ReloadAsync();
        _sync.RequestSync();
    }

    public void Dispose() => _sync.Completed -= OnSyncCompleted;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await _sync.SyncAsync();
            await ReloadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    partial void OnFocusHabitChanged(string? value) => OnPropertyChanged(nameof(HasFocusHabit));

    private async Task ReloadAsync()
    {
        var all = await _recommendations.GetAllAsync();
        var latest = all.Count > 0 ? all[0] : null;
        HasRecommendation = latest is not null;

        Actions.Clear();
        History.Clear();
        if (latest is not null)
        {
            Title = latest.Title;
            Summary = latest.Summary;
            FocusHabit = latest.FocusHabit;
            ModelName = latest.ModelName;
            GeneratedText = Generated(latest.CreatedAt);
            foreach (var action in SplitActions(latest.Recommendation))
            {
                Actions.Add(action);
            }

            foreach (var past in all.Skip(1))
            {
                History.Add(new PastRecommendation(past.Title, Generated(past.CreatedAt), past.Summary));
            }
        }

        OnPropertyChanged(nameof(HasHistory));
        await LoadPatternsAsync();
    }

    /// <summary>Mejor y peor día de la semana en las últimas 4 semanas (las tarjetas pequeñas del mock).</summary>
    private async Task LoadPatternsAsync()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var from = today.AddDays(-(PatternDays - 1));
        var stats = StatisticsCalculator.Calculate(
            await _habits.GetHabitsAsync(),
            await _habits.GetRecordsAsync(from, today),
            from,
            today,
            today,
            TimeZoneInfo.Local);

        var byWeekday = stats.Days
            .Where(day => day.Expected > 0)
            .GroupBy(day => day.Date.DayOfWeek)
            .Select(group => (Day: group.Key, Ratio: (double)group.Sum(d => d.Completed) / group.Sum(d => d.Expected)))
            .OrderByDescending(item => item.Ratio)
            .ToList();

        HasPatterns = byWeekday.Count >= 2;
        if (!HasPatterns)
        {
            return;
        }

        var best = byWeekday[0];
        var worst = byWeekday[^1];
        BestDayValue = HabitFormat.Percent(best.Ratio);
        BestDayLabel = $"Tu mejor día: {DayName(best.Day)}";
        WorstDayValue = HabitFormat.Percent(worst.Ratio);
        WorstDayLabel = $"Atención: {DayName(worst.Day)}";
    }

    private string Generated(DateTimeOffset createdAt)
    {
        var local = TimeZoneInfo.ConvertTime(createdAt, TimeZoneInfo.Local);
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        var when = day == today
            ? "hoy"
            : day == today.AddDays(-1) ? "ayer" : local.ToString("d 'de' MMMM", HomeViewModel.Spanish);
        return $"Generado {when} • {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    internal static IReadOnlyList<string> SplitActions(string recommendation) =>
        recommendation
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('•', '-', '*', ' '))
            .Where(line => line.Length > 0)
            .ToList();

    private static string DayName(DayOfWeek day) =>
        HomeViewModel.Capitalize(HomeViewModel.Spanish.DateTimeFormat.GetDayName(day));

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = _ui.InvokeAsync(ReloadAsync);
}

public sealed record PastRecommendation(string Title, string Generated, string Summary);
