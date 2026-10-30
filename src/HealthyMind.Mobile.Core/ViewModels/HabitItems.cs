using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Fila de "Hábitos de hoy" en el inicio.</summary>
public sealed partial class TodayHabitItem(TodayHabit today) : ObservableObject
{
    public Guid Id => today.Habit.Id;

    public string Name => today.Habit.Name;

    public int Goal => today.Habit.Goal;

    public string Unit => today.Habit.Unit;

    public int Value => today.Value;

    public bool IsCompleted => today.IsCompleted;

    public bool IsStepCounter => today.Habit.TrackingMode == TrackingMode.StepCounter;

    /// <summary>Botones −1 / +1 para metas que se cuentan (8 vasos); no en pasos ni en metas de una sola vez.</summary>
    public bool IsCountable => !IsStepCounter && Goal > 1;

    public bool CanRemoveOne => today.Value > 0;

    /// <summary>"5 / 8 vasos" entre los botones.</summary>
    public string CounterText => $"{today.Value.ToString("N0", HomeViewModel.Spanish)} / {HabitFormat.Amount(today.Habit.Goal, today.Habit.Unit)}";

    public CategoryInfo Category { get; } = CategoryInfo.For(today.Habit.Category);

    public string GoalText => $"Meta: {HabitFormat.Amount(today.Habit.Goal, today.Habit.Unit)}";

    /// <summary>"5 de 8 vasos" cuando hay avance parcial.</summary>
    public string? ProgressText => today.Value > 0 && !today.IsCompleted
        ? $"{today.Value.ToString("N0", HomeViewModel.Spanish)} de {HabitFormat.Amount(today.Habit.Goal, today.Habit.Unit)}"
        : null;

    /// <summary>En los hábitos que se cuentan el avance ya se ve entre los botones −1 / +1.</summary>
    public bool HasProgress => ProgressText is not null && !IsCountable;

    public string StreakText => today.Streak == 1 ? "1 día" : $"{today.Streak} días";

    public bool HasStreak => today.Streak > 0;
}

/// <summary>Tarjeta de la pestaña Hábitos.</summary>
public sealed class HabitListItem(HabitRow habit)
{
    public Guid Id => habit.Id;

    public string Name => habit.Name;

    public bool IsActive => habit.IsActive;

    public bool IsPaused => !habit.IsActive;

    public CategoryInfo Category { get; } = CategoryInfo.For(habit.Category);

    public string GoalText => $"Meta: {HabitFormat.Amount(habit.Goal, habit.Unit)}";

    public string DaysText => HabitFormat.Days(habit.GetScheduledDays());

    public bool IsStepCounter => habit.TrackingMode == TrackingMode.StepCounter;
}

public static class HabitFormat
{
    private static readonly string[] Letters = ["D", "L", "M", "X", "J", "V", "S"];

    public static string Amount(int value, string unit) =>
        $"{value.ToString("N0", HomeViewModel.Spanish)} {unit}";

    /// <summary>"Todos los días", "Lunes a viernes" o "L · X · V".</summary>
    public static string Days(IReadOnlyList<DayOfWeek> days)
    {
        if (days.Count is 0 or 7)
        {
            return "Todos los días";
        }

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (days.Count == 5 && weekdays.All(days.Contains))
        {
            return "Lunes a viernes";
        }

        // Lunes primero, como en el calendario local.
        return string.Join(" · ", days.OrderBy(day => ((int)day + 6) % 7).Select(day => Letters[(int)day]));
    }

    public static string Letter(DayOfWeek day) => Letters[(int)day];

    /// <summary>"1 activo", "3 activos".</summary>
    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    public static string Percent(double ratio) => (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
