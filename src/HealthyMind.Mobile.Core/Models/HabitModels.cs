namespace HealthyMind.Mobile.Core.Models;

// Contratos de hábitos, registros y /api/sync del backend. Los enums viajan como texto.

public enum HabitCategory
{
    Salud = 1,
    Ejercicio = 2,
    Nutricion = 3,
    Lectura = 4,
    Productividad = 5,
    Bienestar = 6,
    Hidratacion = 7,
}

public enum TrackingMode
{
    /// <summary>El usuario registra el valor a mano.</summary>
    Manual = 1,

    /// <summary>Lo llena el contador de pasos del teléfono.</summary>
    StepCounter = 2,
}

public sealed record HabitResponse(
    Guid Id,
    string Name,
    HabitCategory Category,
    int Goal,
    string Unit,
    IReadOnlyList<DayOfWeek> ScheduledDays,
    TrackingMode TrackingMode,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record HabitRecordResponse(
    Guid Id,
    Guid HabitId,
    DateOnly RecordDate,
    int CompletedValue,
    bool Completed,
    string? Notes,
    double? Latitude,
    double? Longitude,
    DateTimeOffset UpdatedAt);

/// <summary>Cambios hechos en el teléfono que se envían a /api/sync/push.</summary>
public sealed record SyncPushRequest(string DeviceId, IReadOnlyList<HabitChange> Habits, IReadOnlyList<HabitRecordChange> Records);

public sealed record HabitChange(
    Guid Id,
    string Name,
    HabitCategory Category,
    int Goal,
    string Unit,
    IReadOnlyList<DayOfWeek> ScheduledDays,
    TrackingMode TrackingMode,
    bool IsActive,
    bool IsDeleted,
    DateTimeOffset UpdatedAt);

public sealed record HabitRecordChange(
    Guid Id,
    Guid HabitId,
    DateOnly RecordDate,
    int CompletedValue,
    string? Notes,
    double? Latitude,
    double? Longitude,
    bool IsDeleted,
    DateTimeOffset UpdatedAt);

public enum SyncItemStatus
{
    /// <summary>Se aplicó en el servidor.</summary>
    Applied = 1,

    /// <summary>El servidor tiene una versión más nueva: manda la del servidor.</summary>
    Stale = 2,

    /// <summary>No se puede aplicar nunca (regla de negocio).</summary>
    Rejected = 3,
}

/// <param name="ServerId">Si difiere de <paramref name="Id"/>, el servidor ya tenía ese dato con otro id.</param>
public sealed record SyncItemResult(Guid Id, SyncItemStatus Status, Guid? ServerId, string? Message);

public sealed record SyncPushResponse(
    DateTimeOffset ServerTime,
    IReadOnlyList<SyncItemResult> Habits,
    IReadOnlyList<SyncItemResult> Records);

public sealed record SyncPullResponse(
    DateTimeOffset ServerTime,
    bool IsFullSnapshot,
    IReadOnlyList<HabitSyncItem> Habits,
    IReadOnlyList<HabitRecordSyncItem> Records);

public sealed record HabitSyncItem(HabitResponse Habit, bool IsDeleted);

public sealed record HabitRecordSyncItem(HabitRecordResponse Record, bool IsDeleted);
