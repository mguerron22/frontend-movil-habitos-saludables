namespace HealthyMind.Mobile.Core.Data;

/// <summary>Hábitos y registros del teléfono. No sabe nada del servidor: eso es de la sincronización.</summary>
public interface IHabitStore
{
    /// <summary>Hábitos vigentes (no borrados), los más nuevos primero.</summary>
    Task<IReadOnlyList<HabitRow>> GetHabitsAsync();

    /// <summary>Incluye borrados pendientes de enviar.</summary>
    Task<HabitRow?> FindHabitAsync(Guid id);

    Task SaveHabitAsync(HabitRow habit);

    /// <summary>Registros vigentes entre dos fechas (inclusive).</summary>
    Task<IReadOnlyList<HabitRecordRow>> GetRecordsAsync(DateOnly from, DateOnly to);

    /// <summary>Registros vigentes de un hábito, del más nuevo al más viejo.</summary>
    Task<IReadOnlyList<HabitRecordRow>> GetRecordsForHabitAsync(Guid habitId);

    /// <summary>El registro de ese día, aunque esté borrado (para reutilizar la fila).</summary>
    Task<HabitRecordRow?> FindRecordAsync(Guid habitId, DateOnly date);

    Task<HabitRecordRow?> FindRecordAsync(Guid id);

    Task SaveRecordAsync(HabitRecordRow record);

    /// <summary>Quita la fila del todo (cuando el servidor ya confirmó la baja o la reemplazó).</summary>
    Task RemoveHabitAsync(Guid id);

    Task RemoveRecordAsync(Guid id);

    Task<IReadOnlyList<HabitRow>> GetAllHabitRowsAsync();

    Task<IReadOnlyList<HabitRecordRow>> GetAllRecordRowsAsync();
}

internal sealed class HabitStore(LocalDatabase database) : IHabitStore
{
    public async Task<IReadOnlyList<HabitRow>> GetHabitsAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<HabitRow>()
            .Where(habit => !habit.IsDeleted)
            .OrderByDescending(habit => habit.CreatedAt)
            .ToListAsync();
    }

    public async Task<HabitRow?> FindHabitAsync(Guid id)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.FindAsync<HabitRow>(id);
    }

    public async Task SaveHabitAsync(HabitRow habit)
    {
        var connection = await database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(habit);
    }

    public async Task<IReadOnlyList<HabitRecordRow>> GetRecordsAsync(DateOnly from, DateOnly to)
    {
        var connection = await database.GetConnectionAsync();
        var start = HabitRecordRow.Format(from);
        var end = HabitRecordRow.Format(to);
        return await connection.QueryAsync<HabitRecordRow>(
            "SELECT * FROM habit_records WHERE IsDeleted = 0 AND RecordDate >= ? AND RecordDate <= ?",
            start,
            end);
    }

    public async Task<IReadOnlyList<HabitRecordRow>> GetRecordsForHabitAsync(Guid habitId)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<HabitRecordRow>()
            .Where(record => record.HabitId == habitId && !record.IsDeleted)
            .OrderByDescending(record => record.RecordDate)
            .ToListAsync();
    }

    public async Task<HabitRecordRow?> FindRecordAsync(Guid habitId, DateOnly date)
    {
        var connection = await database.GetConnectionAsync();
        var day = HabitRecordRow.Format(date);
        return await connection.Table<HabitRecordRow>()
            .Where(record => record.HabitId == habitId && record.RecordDate == day)
            .FirstOrDefaultAsync();
    }

    public async Task<HabitRecordRow?> FindRecordAsync(Guid id)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.FindAsync<HabitRecordRow>(id);
    }

    public async Task SaveRecordAsync(HabitRecordRow record)
    {
        var connection = await database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(record);
    }

    public async Task RemoveHabitAsync(Guid id)
    {
        var connection = await database.GetConnectionAsync();
        await connection.DeleteAsync<HabitRow>(id);
    }

    public async Task RemoveRecordAsync(Guid id)
    {
        var connection = await database.GetConnectionAsync();
        await connection.DeleteAsync<HabitRecordRow>(id);
    }

    public async Task<IReadOnlyList<HabitRow>> GetAllHabitRowsAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<HabitRow>().ToListAsync();
    }

    public async Task<IReadOnlyList<HabitRecordRow>> GetAllRecordRowsAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<HabitRecordRow>().ToListAsync();
    }
}
