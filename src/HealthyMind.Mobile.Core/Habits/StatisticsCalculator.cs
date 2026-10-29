using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.Habits;

/// <summary>Cumplimiento de un día.</summary>
public sealed record DayStats(DateOnly Date, int Expected, int Completed)
{
    public double Ratio => Expected == 0 ? 0 : (double)Completed / Expected;
}

/// <summary>Cumplimiento de un hábito en el periodo.</summary>
public sealed record HabitStats(HabitRow Habit, int Expected, int Completed)
{
    public double Ratio => Expected == 0 ? 0 : (double)Completed / Expected;
}

/// <summary>Estadísticas de un periodo (RF04): días, hábitos y totales.</summary>
public sealed record PeriodStats(DateOnly From, DateOnly To, IReadOnlyList<DayStats> Days, IReadOnlyList<HabitStats> Habits)
{
    public int Expected => Days.Sum(day => day.Expected);

    public int Completed => Days.Sum(day => day.Completed);

    public double Ratio => Expected == 0 ? 0 : (double)Completed / Expected;
}

/// <summary>Peso de una categoría entre los hábitos activos (la dona "Distribución por dimensión").</summary>
public sealed record CategoryShare(HabitCategory Category, int Habits, double Share);

/// <summary>
/// Calcula el cumplimiento con la misma regla que el backend (ComplianceCalculator), pero sobre los datos del
/// teléfono para que las estadísticas funcionen sin conexión.
/// </summary>
/// <remarks>
/// Un hábito "se esperaba" un día si ese día tiene registro, o si estaba activo, programado y ya existía.
/// Los días que aún no llegan no cuentan.
/// </remarks>
public static class StatisticsCalculator
{
    public static PeriodStats Calculate(
        IReadOnlyList<HabitRow> habits,
        IReadOnlyList<HabitRecordRow> records,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        var lastDay = to > today ? today : to;
        var live = habits.Where(habit => !habit.IsDeleted).ToList();
        var byKey = records
            .Where(record => !record.IsDeleted)
            .GroupBy(record => (record.HabitId, record.RecordDate))
            .ToDictionary(group => group.Key, group => group.First());

        var days = new List<DayStats>();
        var perHabit = live.ToDictionary(habit => habit.Id, _ => (Expected: 0, Completed: 0));

        for (var date = from; date <= lastDay; date = date.AddDays(1))
        {
            var expected = 0;
            var completed = 0;
            var key = HabitRecordRow.Format(date);

            foreach (var habit in live)
            {
                byKey.TryGetValue((habit.Id, key), out var record);
                if (!WasExpected(habit, record, date, timeZone))
                {
                    continue;
                }

                var done = record?.Completed == true;
                expected++;
                completed += done ? 1 : 0;
                var current = perHabit[habit.Id];
                perHabit[habit.Id] = (current.Expected + 1, current.Completed + (done ? 1 : 0));
            }

            days.Add(new DayStats(date, expected, completed));
        }

        var habitStats = live
            .Select(habit => new HabitStats(habit, perHabit[habit.Id].Expected, perHabit[habit.Id].Completed))
            .Where(stats => stats.Expected > 0 || stats.Habit.IsActive)
            .OrderByDescending(stats => stats.Ratio)
            .ThenBy(stats => stats.Habit.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new PeriodStats(from, to, days, habitStats);
    }

    /// <summary>Distribución de hábitos activos por categoría, de mayor a menor.</summary>
    public static IReadOnlyList<CategoryShare> Distribution(IReadOnlyList<HabitRow> habits)
    {
        var active = habits.Where(habit => habit.IsActive && !habit.IsDeleted).ToList();
        if (active.Count == 0)
        {
            return [];
        }

        return active
            .GroupBy(habit => habit.Category)
            .Select(group => new CategoryShare(group.Key, group.Count(), (double)group.Count() / active.Count))
            .OrderByDescending(share => share.Habits)
            .ThenBy(share => share.Category)
            .ToList();
    }

    /// <summary>Lunes de la semana que contiene la fecha.</summary>
    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static bool WasExpected(HabitRow habit, HabitRecordRow? record, DateOnly date, TimeZoneInfo timeZone)
    {
        if (record is not null)
        {
            return true;
        }

        var createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(habit.CreatedAt, timeZone).DateTime);
        return habit.IsActive && habit.IsScheduledOn(date) && date >= createdOn;
    }
}
