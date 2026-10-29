using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class StatisticsTests
{
    // Miércoles 28 de octubre de 2026.
    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Days_that_have_not_arrived_do_not_count()
    {
        var water = Habit("Tomar agua");
        var records = new[] { Record(water, Monday, completed: true), Record(water, Monday.AddDays(1), completed: false) };

        var stats = StatisticsCalculator.Calculate([water], records, Monday, Monday.AddDays(6), Today, Utc);

        Assert.Equal(3, stats.Days.Count);
        Assert.Equal(3, stats.Expected);
        Assert.Equal(1, stats.Completed);
    }

    [Fact]
    public void Paused_habits_without_records_and_deleted_habits_are_ignored()
    {
        var water = Habit("Tomar agua");
        var paused = Habit("Leer", isActive: false);
        var deleted = Habit("Caminar", isDeleted: true);

        var stats = StatisticsCalculator.Calculate([water, paused, deleted], [], Monday, Today, Today, Utc);

        Assert.Equal("Tomar agua", Assert.Single(stats.Habits).Habit.Name);
        Assert.Equal(3, stats.Expected);
    }

    [Fact]
    public void A_record_counts_even_if_the_habit_was_paused_later()
    {
        var paused = Habit("Leer", isActive: false);

        var stats = StatisticsCalculator.Calculate([paused], [Record(paused, Monday, completed: true)], Monday, Today, Today, Utc);

        Assert.Equal(1, stats.Expected);
        Assert.Equal(1, stats.Completed);
    }

    [Fact]
    public void Distribution_only_uses_active_habits()
    {
        var habits = new[]
        {
            Habit("Tomar agua", HabitCategory.Hidratacion),
            Habit("Más agua", HabitCategory.Hidratacion),
            Habit("Leer", HabitCategory.Lectura),
            Habit("Correr", HabitCategory.Ejercicio, isActive: false),
        };

        var shares = StatisticsCalculator.Distribution(habits);

        Assert.Equal([HabitCategory.Hidratacion, HabitCategory.Lectura], shares.Select(share => share.Category));
        Assert.Equal(2d / 3, shares[0].Share, 3);
    }

    [Fact]
    public void Weeks_start_on_monday() => Assert.Equal(Monday, StatisticsCalculator.StartOfWeek(Today));

    [Fact]
    public void Recommendation_actions_come_one_per_line_without_bullets()
    {
        var actions = InsightsViewModel.SplitActions("• Toma un vaso al despertar\n\n- Camina 10 minutos\n  * Lee antes de dormir  ");

        Assert.Equal(["Toma un vaso al despertar", "Camina 10 minutos", "Lee antes de dormir"], actions);
    }

    private static HabitRow Habit(string name, HabitCategory category = HabitCategory.Salud, bool isActive = true, bool isDeleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Category = category,
        Goal = 1,
        Unit = "vez",
        IsActive = isActive,
        IsDeleted = isDeleted,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static HabitRecordRow Record(HabitRow habit, DateOnly date, bool completed) => new()
    {
        Id = Guid.NewGuid(),
        HabitId = habit.Id,
        RecordDate = HabitRecordRow.Format(date),
        CompletedValue = completed ? 1 : 0,
        Completed = completed,
    };
}
