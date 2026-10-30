using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Crear o editar un hábito (RF02): nombre, categoría, meta, unidad, días y forma de medirlo.</summary>
public sealed partial class HabitEditorViewModel : ViewModelBase
{
    private readonly IHabitService _habits;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private Guid? _habitId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseText))]
    private bool _isActive = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _goal = "1";

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private bool _usesStepCounter;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _goalError;

    [ObservableProperty]
    private string? _unitError;

    public HabitEditorViewModel(IHabitService habits, INavigationService navigation, IDialogService dialogs)
    {
        _habits = habits;
        _navigation = navigation;
        _dialogs = dialogs;

        foreach (var category in CategoryInfo.All)
        {
            Categories.Add(new CategoryOption(category));
        }

        // Lunes primero, como en el mock (L M X J V S D).
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            Days.Add(new DayOption(day) { IsSelected = true });
        }

        Categories[0].IsSelected = true;
    }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<DayOption> Days { get; } = [];

    public string Title => IsEditing ? "Editar hábito" : "Nuevo hábito";

    public string SaveText => IsEditing ? "Guardar cambios" : "Crear hábito";

    public string PauseText => IsActive ? "Pausar hábito" : "Reactivar hábito";

    /// <summary>El interruptor de pasos solo se muestra con la categoría Ejercicio.</summary>
    public bool CanCountSteps => HabitRules.SupportsStepCounter(SelectedCategory);

    private HabitCategory SelectedCategory => Categories.First(option => option.IsSelected).Info.Category;

    public async Task LoadAsync(Guid? habitId)
    {
        _habitId = habitId;
        IsEditing = habitId is not null;
        if (habitId is null)
        {
            return;
        }

        var habit = await _habits.GetAsync(habitId.Value);
        if (habit is null)
        {
            await _dialogs.AlertAsync("Hábito", "Este hábito ya no existe.");
            await _navigation.GoBackAsync();
            return;
        }

        Name = habit.Name;
        Goal = habit.Goal.ToString(CultureInfo.InvariantCulture);
        Unit = habit.Unit;
        UsesStepCounter = habit.TrackingMode == TrackingMode.StepCounter;
        IsActive = habit.IsActive;

        foreach (var option in Categories)
        {
            option.IsSelected = option.Info.Category == habit.Category;
        }

        OnPropertyChanged(nameof(CanCountSteps));

        var scheduled = habit.GetScheduledDays();
        foreach (var day in Days)
        {
            day.IsSelected = scheduled.Count == 0 || scheduled.Contains(day.Day);
        }
    }

    /// <summary>Con contador de pasos la unidad es siempre "pasos" (RF06).</summary>
    partial void OnUsesStepCounterChanged(bool value)
    {
        if (value)
        {
            Unit = HabitRules.StepUnit;
            if (Goal is "1" or "")
            {
                Goal = "6000";
            }
        }
    }

    [RelayCommand]
    private void SelectCategory(CategoryOption option)
    {
        foreach (var item in Categories)
        {
            item.IsSelected = item == option;
        }

        OnPropertyChanged(nameof(CanCountSteps));

        // Al salir de Ejercicio se vuelve a medir a mano; "pasos" ya no tiene sentido como unidad.
        if (!CanCountSteps && UsesStepCounter)
        {
            UsesStepCounter = false;
            Unit = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleDay(DayOption day)
    {
        // Al menos un día: un hábito sin días no aparecería nunca.
        if (day.IsSelected && Days.Count(item => item.IsSelected) == 1)
        {
            return;
        }

        day.IsSelected = !day.IsSelected;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        NameError = GoalError = UnitError = null;
        if (!int.TryParse(Goal.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var goal))
        {
            goal = 0;
        }

        var draft = new HabitDraft(
            _habitId,
            Name,
            SelectedCategory,
            goal,
            Unit,
            Days.Where(day => day.IsSelected).Select(day => day.Day).ToList(),
            UsesStepCounter && CanCountSteps ? TrackingMode.StepCounter : TrackingMode.Manual);

        if (await RunAsync(() => _habits.SaveAsync(draft)))
        {
            await _navigation.GoBackAsync();
        }
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (_habitId is not { } id)
        {
            return;
        }

        if (await RunAsync(() => _habits.SetActiveAsync(id, !IsActive)))
        {
            await _navigation.GoBackAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_habitId is not { } id)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Eliminar hábito",
            $"Se borrará «{Name}» con todo su historial. No se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (confirmed && await RunAsync(() => _habits.DeleteAsync(id)))
        {
            await _navigation.GoBackAsync();
        }
    }

    [RelayCommand]
    private Task CancelAsync() => _navigation.GoBackAsync();

    protected override void OnUserMessage(UserMessageException exception)
    {
        if (exception is not HabitValidationException validation)
        {
            base.OnUserMessage(exception);
            return;
        }

        NameError = validation.Errors.GetValueOrDefault(nameof(HabitDraft.Name));
        GoalError = validation.Errors.GetValueOrDefault(nameof(HabitDraft.Goal));
        UnitError = validation.Errors.GetValueOrDefault(nameof(HabitDraft.Unit));
        ErrorMessage = "Revisa los campos marcados.";
    }
}

public sealed partial class CategoryOption(CategoryInfo info) : ObservableObject
{
    public CategoryInfo Info { get; } = info;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class DayOption(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public string Letter => HabitFormat.Letter(Day);

    [ObservableProperty]
    private bool _isSelected;
}
