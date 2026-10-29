using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Habits;

/// <summary>Lo que el usuario escribe en el formulario de hábito.</summary>
public sealed record HabitDraft(
    Guid? Id,
    string Name,
    HabitCategory Category,
    int Goal,
    string Unit,
    IReadOnlyList<DayOfWeek> Days,
    TrackingMode TrackingMode);

/// <summary>
/// Hábitos y avance diario (RF02, RF03). Todo se guarda primero en el teléfono y se encola para el servidor:
/// funciona igual con o sin internet.
/// </summary>
public interface IHabitService
{
    Task<IReadOnlyList<HabitRow>> GetHabitsAsync();

    Task<HabitRow?> GetAsync(Guid id);

    /// <exception cref="HabitValidationException">El borrador no cumple las reglas.</exception>
    Task<HabitRow> SaveAsync(HabitDraft draft);

    Task SetActiveAsync(Guid id, bool isActive);

    /// <summary>Borra el hábito y sus registros.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Crea o actualiza el avance del día (0 deja el día sin cumplir).</summary>
    Task<HabitRecordRow> RecordAsync(Guid habitId, DateOnly date, int value);

    /// <summary>Hábitos de hoy con su avance y racha, para la pantalla de inicio.</summary>
    Task<TodaySummary> GetTodayAsync();
}

public sealed class HabitValidationException(IReadOnlyDictionary<string, string> errors)
    : UserMessageException(errors.Values.FirstOrDefault() ?? "El hábito no es válido.")
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

public sealed class HabitService(
    IHabitStore store,
    ISyncOutbox outbox,
    ISyncService sync,
    TimeProvider timeProvider) : IHabitService
{
    /// <summary>Días hacia atrás que se miran para calcular rachas.</summary>
    private const int StreakLookbackDays = 120;

    public Task<IReadOnlyList<HabitRow>> GetHabitsAsync() => store.GetHabitsAsync();

    public async Task<HabitRow?> GetAsync(Guid id)
    {
        var habit = await store.FindHabitAsync(id);
        return habit is { IsDeleted: false } ? habit : null;
    }

    public async Task<HabitRow> SaveAsync(HabitDraft draft)
    {
        var others = (await store.GetHabitsAsync()).Where(habit => habit.Id != draft.Id).Select(habit => habit.Name);
        var errors = HabitRules.Validate(draft, others);
        if (errors.Count > 0)
        {
            throw new HabitValidationException(errors);
        }

        var now = timeProvider.GetUtcNow();
        var habit = draft.Id is { } id ? await GetAsync(id) : null;
        habit ??= new HabitRow { Id = Guid.NewGuid(), CreatedAt = now, IsActive = true };

        habit.Name = draft.Name.Trim();
        habit.Category = draft.Category;
        habit.Goal = draft.Goal;
        habit.Unit = draft.Unit.Trim();
        habit.TrackingMode = draft.TrackingMode;
        habit.SetScheduledDays(draft.Days);
        habit.UpdatedAt = now;

        await store.SaveHabitAsync(habit);
        await QueueAsync(PendingEntity.Habit, habit.Id);
        return habit;
    }

    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var habit = await GetAsync(id) ?? throw new UserMessageException("El hábito no existe.");
        habit.IsActive = isActive;
        habit.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveHabitAsync(habit);
        await QueueAsync(PendingEntity.Habit, habit.Id);
    }

    public async Task DeleteAsync(Guid id)
    {
        var habit = await GetAsync(id) ?? throw new UserMessageException("El hábito no existe.");
        habit.IsDeleted = true;
        habit.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveHabitAsync(habit);

        // El servidor borra los registros junto con el hábito: no hace falta enviarlos uno por uno.
        foreach (var record in await store.GetRecordsForHabitAsync(id))
        {
            await outbox.RemoveAsync(PendingEntity.Record, record.Id);
            await store.RemoveRecordAsync(record.Id);
        }

        await QueueAsync(PendingEntity.Habit, habit.Id);
    }

    public async Task<HabitRecordRow> RecordAsync(Guid habitId, DateOnly date, int value)
    {
        var habit = await GetAsync(habitId) ?? throw new UserMessageException("El hábito no existe.");
        if (date > Today())
        {
            throw new UserMessageException("No se puede registrar una fecha futura.");
        }

        if (value is < 0 or > HabitRules.MaxRecordValue)
        {
            throw new UserMessageException("El valor no es válido.");
        }

        // Si ya hubo una fila ese día (aunque esté borrada sin enviar) se reutiliza: hay un solo registro por día.
        var record = await store.FindRecordAsync(habitId, date);
        if (record is null)
        {
            if (!habit.IsActive)
            {
                throw new UserMessageException("El hábito está pausado. Actívalo para registrar avance.");
            }

            record = new HabitRecordRow { Id = Guid.NewGuid(), HabitId = habitId, RecordDate = HabitRecordRow.Format(date) };
        }

        record.CompletedValue = value;
        record.Completed = value >= habit.Goal;
        record.IsDeleted = false;
        record.UpdatedAt = timeProvider.GetUtcNow();

        await store.SaveRecordAsync(record);
        await QueueAsync(PendingEntity.Record, record.Id);
        return record;
    }

    public async Task<TodaySummary> GetTodayAsync()
    {
        var today = Today();
        var habits = await store.GetHabitsAsync();
        var records = await store.GetRecordsAsync(today.AddDays(-StreakLookbackDays), today);
        return HabitInsights.Today(habits, records, today);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private async Task QueueAsync(PendingEntity entity, Guid id)
    {
        await outbox.EnqueueAsync(entity, id);
        sync.RequestSync();
    }
}
