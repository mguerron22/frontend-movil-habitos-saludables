using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>
/// Inicio: saludo, "Energía del día" y los hábitos de hoy. Todo sale del teléfono, así que funciona sin internet;
/// marcar un hábito se guarda local y se sincroniza solo.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase, IDisposable
{
    public static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    private readonly ISessionManager _session;
    private readonly IHabitService _habits;
    private readonly ISyncService _sync;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _timeProvider;
    private readonly StepTracker _steps;
    private readonly RecordLocation _location;
    private readonly IAppNotifications _notifications;
    private readonly SemaphoreSlim _counterGate = new(1, 1);

    [ObservableProperty]
    private string _greeting = string.Empty;

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _isOfflineSession;

    [ObservableProperty]
    private string _energyTitle = string.Empty;

    [ObservableProperty]
    private string _motivation = string.Empty;

    /// <summary>De 0 a 1, para el anillo y la barra.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressPercent = "0%";

    [ObservableProperty]
    private string _ringText = "0/0";

    [ObservableProperty]
    private string _activeHabitsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingSync))]
    private int _pendingSync;

    [ObservableProperty]
    private bool _hasHabitsToday;

    [ObservableProperty]
    private bool _hasNoHabits;

    [ObservableProperty]
    private bool _isRefreshing;

    public HomeViewModel(
        ISessionManager session,
        IHabitService habits,
        ISyncService sync,
        INavigationService navigation,
        IDialogService dialogs,
        IUiDispatcher ui,
        TimeProvider timeProvider,
        StepTracker steps,
        RecordLocation location,
        IAppNotifications notifications)
    {
        _session = session;
        _habits = habits;
        _sync = sync;
        _navigation = navigation;
        _dialogs = dialogs;
        _ui = ui;
        _timeProvider = timeProvider;
        _steps = steps;
        _location = location;
        _notifications = notifications;
        _session.OnlineRestored += OnOnlineRestored;
        _sync.Completed += OnSyncCompleted;
    }

    public ObservableCollection<TodayHabitItem> TodayHabits { get; } = [];

    public bool HasPendingSync => PendingSync > 0;

    public string PendingSyncText => PendingSync == 1 ? "1 cambio por sincronizar" : $"{PendingSync} cambios por sincronizar";

    public async Task LoadAsync()
    {
        var now = _timeProvider.GetLocalNow();
        var user = _session.CurrentUser;

        Greeting = $"{GreetingFor(now.Hour)}, {user?.FirstName ?? "bienvenido"}!";
        TodayText = Capitalize(now.ToString("dddd, d 'de' MMMM", Spanish));
        Initials = user?.Initials ?? string.Empty;
        IsOfflineSession = _session.IsOfflineSession;

        await UpdateStepsAsync();
        await ReloadHabitsAsync();
        _sync.RequestSync();

        // Sin permiso no llegan los avisos de la IA. Android pregunta una sola vez (o dos, si la primera dijo que no).
        await _notifications.EnsurePermissionAsync();
    }

    public void Dispose()
    {
        _session.OnlineRestored -= OnOnlineRestored;
        _sync.Completed -= OnSyncCompleted;
        _counterGate.Dispose();
    }

    /// <summary>Check del mock: marca la meta completa, o la desmarca si ya estaba cumplida.</summary>
    [RelayCommand]
    private async Task ToggleAsync(TodayHabitItem item)
    {
        if (item.IsStepCounter && !item.IsCompleted)
        {
            await RecordValueAsync(item); // Los pasos se cuentan, no se "marcan".
            return;
        }

        await RunAsync(async () =>
        {
            await _habits.RecordAsync(item.Id, Today(), item.IsCompleted ? 0 : item.Goal, await _location.CurrentIfEnabledAsync());
            await ReloadHabitsAsync();
        });
    }

    /// <summary>Tocar la tarjeta: anotar cuánto se hizo (5 de 8 vasos).</summary>
    [RelayCommand]
    private async Task RecordValueAsync(TodayHabitItem item)
    {
        var answer = await _dialogs.PromptAsync(
            item.Name,
            $"¿Cuánto llevas hoy? Meta: {HabitFormat.Amount(item.Goal, item.Unit)}.",
            item.Value > 0 ? item.Value.ToString(CultureInfo.InvariantCulture) : null,
            item.Unit,
            PromptKind.Number);

        if (answer is null)
        {
            return;
        }

        if (!int.TryParse(answer.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            await _dialogs.AlertAsync(item.Name, "Escribe un número entero, por ejemplo 5.");
            return;
        }

        await RunAsync(async () =>
        {
            await _habits.RecordAsync(item.Id, Today(), value, await _location.CurrentIfEnabledAsync());
            await ReloadHabitsAsync();
        });
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AddOneAsync(TodayHabitItem item) => ChangeByAsync(item, 1);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RemoveOneAsync(TodayHabitItem item) => ChangeByAsync(item, -1);

    [RelayCommand]
    private Task AddHabitAsync() => _navigation.GoToHabitEditorAsync();

    [RelayCommand]
    private Task SeeAllAsync() => _navigation.GoToHabitsAsync();

    /// <summary>Deslizar hacia abajo: sincroniza ya.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await UpdateStepsAsync();
            await _sync.SyncAsync();
            await ReloadHabitsAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task ReloadHabitsAsync()
    {
        var summary = await _habits.GetTodayAsync();

        TodayHabits.Clear();
        foreach (var habit in summary.Habits)
        {
            TodayHabits.Add(new TodayHabitItem(habit));
        }

        EnergyTitle = summary.Expected == 1
            ? $"{summary.Completed} de 1 hábito"
            : $"{summary.Completed} de {summary.Expected} hábitos";
        RingText = $"{summary.Completed}/{summary.Expected}";
        Progress = summary.Progress;
        ProgressPercent = HabitFormat.Percent(summary.Progress);
        Motivation = HabitInsights.Motivation(summary);
        ActiveHabitsText = HabitFormat.Count(summary.ActiveHabits, "activo", "activos");
        HasHabitsToday = summary.Expected > 0;
        HasNoHabits = summary.ActiveHabits == 0;
        PendingSync = await _sync.PendingCountAsync();
        OnPropertyChanged(nameof(PendingSyncText));
    }

    /// <summary>Los hábitos de pasos se llenan solos; si el sensor falla, se siguen pudiendo anotar a mano.</summary>
    private async Task UpdateStepsAsync()
    {
        try
        {
            await _steps.UpdateAsync();
        }
        catch (UserMessageException)
        {
            // P. ej. el hábito se pausó desde otro teléfono justo ahora: se ignora hasta la próxima lectura.
        }
    }

    /// <summary>
    /// −1 / +1 sin teclado. Los toques rápidos se encolan en vez de perderse: cada uno parte de lo ya guardado.
    /// </summary>
    private async Task ChangeByAsync(TodayHabitItem item, int amount)
    {
        await _counterGate.WaitAsync();
        try
        {
            await _habits.AddAsync(item.Id, Today(), amount, await _location.CurrentIfEnabledAsync());
            await ReloadHabitsAsync();
        }
        catch (UserMessageException exception)
        {
            await _dialogs.AlertAsync(item.Name, exception.Message);
        }
        finally
        {
            _counterGate.Release();
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    private void OnOnlineRestored(object? sender, EventArgs e) =>
        _ = _ui.InvokeAsync(() =>
        {
            IsOfflineSession = false;
            return Task.CompletedTask;
        });

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e) =>
        _ = _ui.InvokeAsync(async () =>
        {
            await ReloadHabitsAsync();
            if (e.Rejected.Count > 0)
            {
                await _dialogs.AlertAsync("Algunos cambios no se guardaron", string.Join("\n", e.Rejected));
            }
        });

    internal static string GreetingFor(int hour) => hour switch
    {
        < 12 => "¡Buenos días",
        < 19 => "¡Buenas tardes",
        _ => "¡Buenas noches",
    };

    internal static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Spanish) + text[1..];
}
