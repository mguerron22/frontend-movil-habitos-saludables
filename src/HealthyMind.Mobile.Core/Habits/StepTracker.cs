using System.Globalization;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Habits;

/// <summary>Contador de pasos del teléfono (en Android, TYPE_STEP_COUNTER).</summary>
public interface IStepSensor
{
    /// <summary>El teléfono tiene contador de pasos.</summary>
    bool IsSupported { get; }

    /// <summary>Pide el permiso de actividad física si hace falta.</summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Pasos acumulados desde que se encendió el teléfono, o null si no se pudo leer.</summary>
    Task<long?> ReadTotalAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pasos del día a partir del contador acumulado. El sensor cuenta desde que se encendió el teléfono:
/// se guarda la última lectura y se suma la diferencia; si el contador bajó, el teléfono se reinició.
/// </summary>
/// <remarks>Los pasos entre la última lectura de ayer y la medianoche no se pueden repartir: no cuentan para hoy.</remarks>
public sealed record StepDay(DateOnly Date, long LastCounter, int Steps)
{
    public static StepDay Advance(StepDay? previous, DateOnly today, long counter)
    {
        if (previous is null || previous.Date != today)
        {
            return new StepDay(today, counter, 0);
        }

        var delta = counter >= previous.LastCounter ? counter - previous.LastCounter : counter;
        return previous with { LastCounter = counter, Steps = (int)Math.Min(previous.Steps + delta, HabitRules.MaxRecordValue) };
    }

    public string Serialize() =>
        string.Join('|', Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), LastCounter.ToString(CultureInfo.InvariantCulture), Steps.ToString(CultureInfo.InvariantCulture));

    public static StepDay? Parse(string? value)
    {
        var parts = value?.Split('|');
        return parts is { Length: 3 }
               && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
               && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var counter)
               && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var steps)
            ? new StepDay(date, counter, steps)
            : null;
    }
}

/// <summary>Lleva los pasos del teléfono a los hábitos "Contar pasos automáticamente" de hoy (RF06).</summary>
public sealed class StepTracker(IStepSensor sensor, IHabitService habits, IAppSettings settings, TimeProvider timeProvider)
{
    /// <returns>Pasos de hoy, o null si no hay sensor, no hay permiso o no hay hábitos de pasos.</returns>
    public async Task<int?> UpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!sensor.IsSupported)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var stepHabits = (await habits.GetHabitsAsync())
            .Where(habit => habit is { IsActive: true, IsDeleted: false, TrackingMode: TrackingMode.StepCounter })
            .ToList();
        if (stepHabits.Count == 0 || !await sensor.EnsurePermissionAsync())
        {
            return null;
        }

        var counter = await sensor.ReadTotalAsync(cancellationToken);
        if (counter is null)
        {
            return null;
        }

        var day = StepDay.Advance(StepDay.Parse(settings.StepCounterState), today, counter.Value);
        settings.StepCounterState = day.Serialize();

        var summary = await habits.GetTodayAsync();
        foreach (var habit in stepHabits)
        {
            // Nunca se baja un valor: pudo registrarse desde otro teléfono o a mano.
            var current = summary.Habits.FirstOrDefault(item => item.Habit.Id == habit.Id)?.Value ?? 0;
            if (day.Steps > current && habit.IsScheduledOn(today))
            {
                await habits.RecordAsync(habit.Id, today, day.Steps);
            }
        }

        return day.Steps;
    }
}
