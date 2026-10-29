using HealthyMind.Mobile.Core.Models;
using SQLite;

namespace HealthyMind.Mobile.Core.Data;

/// <summary>Hábito guardado en el teléfono. Es la fuente de verdad de la app: se lee siempre de aquí.</summary>
[Table("habits")]
public sealed class HabitRow
{
    /// <summary>Lo genera el teléfono al crear; el backend lo respeta.</summary>
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public HabitCategory Category { get; set; }

    public int Goal { get; set; }

    public string Unit { get; set; } = string.Empty;

    /// <summary>Días como números de DayOfWeek separados por coma ("1,3,5"). Vacío = todos los días.</summary>
    public string ScheduledDays { get; set; } = string.Empty;

    public TrackingMode TrackingMode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cuándo se hizo el último cambio; decide quién gana en un conflicto con otro dispositivo.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Baja pendiente de enviar: se oculta, pero la fila vive hasta que el servidor la confirme.</summary>
    public bool IsDeleted { get; set; }

    public IReadOnlyList<DayOfWeek> GetScheduledDays() =>
        string.IsNullOrEmpty(ScheduledDays)
            ? []
            : ScheduledDays.Split(',').Select(day => (DayOfWeek)int.Parse(day, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    public void SetScheduledDays(IEnumerable<DayOfWeek> days)
    {
        var distinct = days.Distinct().Order().ToList();
        // Los 7 días es lo mismo que "todos": se guarda vacío, como lo entiende el backend.
        ScheduledDays = distinct.Count is 0 or 7
            ? string.Empty
            : string.Join(',', distinct.Select(day => ((int)day).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public bool IsScheduledOn(DateOnly date)
    {
        var days = GetScheduledDays();
        return days.Count == 0 || days.Contains(date.DayOfWeek);
    }
}

/// <summary>Avance de un hábito en un día.</summary>
[Table("habit_records")]
public sealed class HabitRecordRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed(Name = "ux_record_habit_date", Order = 1, Unique = true)]
    public Guid HabitId { get; set; }

    /// <summary>yyyy-MM-dd: SQLite no tiene tipo fecha y así se ordena y compara bien como texto.</summary>
    [Indexed(Name = "ux_record_habit_date", Order = 2, Unique = true)]
    public string RecordDate { get; set; } = string.Empty;

    public int CompletedValue { get; set; }

    public bool Completed { get; set; }

    public string? Notes { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateOnly Date => DateOnly.ParseExact(RecordDate, DateFormat, System.Globalization.CultureInfo.InvariantCulture);

    public const string DateFormat = "yyyy-MM-dd";

    public static string Format(DateOnly date) => date.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);
}

public enum PendingEntity
{
    Habit = 1,
    Record = 2,
}

/// <summary>
/// Cola de cambios por enviar. Guarda solo qué entidad cambió: al enviar se manda su estado actual,
/// así diez cambios seguidos al mismo hábito viajan como uno.
/// </summary>
[Table("pending_changes")]
public sealed class PendingChangeRow
{
    /// <summary>"Habit:{id}" o "Record:{id}": una sola fila por entidad.</summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public PendingEntity Entity { get; set; }

    public Guid EntityId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public static string KeyFor(PendingEntity entity, Guid id) => $"{entity}:{id}";
}

/// <summary>Valores sueltos de la sincronización (por ahora, el cursor del último pull).</summary>
[Table("sync_state")]
public sealed class SyncStateRow
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
