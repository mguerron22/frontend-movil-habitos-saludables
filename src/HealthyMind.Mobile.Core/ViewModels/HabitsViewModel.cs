using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Pestaña Hábitos (RF02): todos los hábitos, con filtro por categoría.</summary>
public sealed partial class HabitsViewModel : ViewModelBase, IDisposable
{
    private readonly IHabitService _habits;
    private readonly ISyncService _sync;
    private readonly ISessionManager _session;
    private readonly INavigationService _navigation;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _showPaused = true;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    public HabitsViewModel(
        IHabitService habits,
        ISyncService sync,
        ISessionManager session,
        INavigationService navigation,
        IUiDispatcher ui)
    {
        _habits = habits;
        _sync = sync;
        _session = session;
        _navigation = navigation;
        _ui = ui;
        _sync.Completed += OnSyncCompleted;

        Filters.Add(new CategoryFilter(null, "Todos") { IsSelected = true });
        foreach (var category in CategoryInfo.All)
        {
            Filters.Add(new CategoryFilter(category.Category, category.Name));
        }
    }

    public ObservableCollection<CategoryFilter> Filters { get; } = [];

    public ObservableCollection<HabitListItem> Habits { get; } = [];

    public async Task LoadAsync()
    {
        Initials = _session.CurrentUser?.Initials ?? string.Empty;
        await ReloadAsync();
    }

    public void Dispose() => _sync.Completed -= OnSyncCompleted;

    [RelayCommand]
    private async Task SelectFilterAsync(CategoryFilter filter)
    {
        foreach (var item in Filters)
        {
            item.IsSelected = item == filter;
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToHabitEditorAsync();

    [RelayCommand]
    private Task OpenAsync(HabitListItem item) => _navigation.GoToHabitEditorAsync(item.Id);

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

    async partial void OnShowPausedChanged(bool value) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var all = await _habits.GetHabitsAsync();
        var category = Filters.FirstOrDefault(filter => filter.IsSelected)?.Category;

        var visible = all
            .Where(habit => category is null || habit.Category == category)
            .Where(habit => ShowPaused || habit.IsActive)
            .OrderByDescending(habit => habit.IsActive)
            .ThenBy(habit => habit.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Habits.Clear();
        foreach (var habit in visible)
        {
            Habits.Add(new HabitListItem(habit));
        }

        var active = all.Count(habit => habit.IsActive);
        var paused = all.Count - active;
        Summary = paused == 0
            ? HabitFormat.Count(active, "activo", "activos")
            : $"{HabitFormat.Count(active, "activo", "activos")} · {HabitFormat.Count(paused, "pausado", "pausados")}";
        IsEmpty = Habits.Count == 0;
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) => _ = _ui.InvokeAsync(ReloadAsync);
}

/// <summary>Chip de filtro por categoría.</summary>
public sealed partial class CategoryFilter(HabitCategory? category, string name) : ObservableObject
{
    public HabitCategory? Category { get; } = category;

    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}
