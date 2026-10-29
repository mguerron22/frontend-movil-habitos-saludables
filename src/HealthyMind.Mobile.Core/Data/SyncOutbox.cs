using System.Globalization;

namespace HealthyMind.Mobile.Core.Data;

/// <summary>Lo que falta enviar al servidor y desde cuándo pedir cambios.</summary>
public interface ISyncOutbox
{
    Task EnqueueAsync(PendingEntity entity, Guid id);

    Task<IReadOnlyList<PendingChangeRow>> GetPendingAsync();

    Task<int> CountAsync();

    Task RemoveAsync(PendingEntity entity, Guid id);

    Task<bool> IsPendingAsync(PendingEntity entity, Guid id);

    /// <summary>El <c>serverTime</c> del último pull; null si nunca se descargó nada.</summary>
    Task<DateTimeOffset?> GetPullCursorAsync();

    Task SetPullCursorAsync(DateTimeOffset cursor);
}

internal sealed class SyncOutbox(LocalDatabase database, TimeProvider timeProvider) : ISyncOutbox
{
    private const string PullCursorKey = "pull_cursor";

    public async Task EnqueueAsync(PendingEntity entity, Guid id)
    {
        var connection = await database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(new PendingChangeRow
        {
            Key = PendingChangeRow.KeyFor(entity, id),
            Entity = entity,
            EntityId = id,
            QueuedAt = timeProvider.GetUtcNow(),
        });
    }

    public async Task<IReadOnlyList<PendingChangeRow>> GetPendingAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<PendingChangeRow>().OrderBy(change => change.QueuedAt).ToListAsync();
    }

    public async Task<int> CountAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<PendingChangeRow>().CountAsync();
    }

    public async Task RemoveAsync(PendingEntity entity, Guid id)
    {
        var connection = await database.GetConnectionAsync();
        await connection.DeleteAsync<PendingChangeRow>(PendingChangeRow.KeyFor(entity, id));
    }

    public async Task<bool> IsPendingAsync(PendingEntity entity, Guid id)
    {
        var connection = await database.GetConnectionAsync();
        return await connection.FindAsync<PendingChangeRow>(PendingChangeRow.KeyFor(entity, id)) is not null;
    }

    public async Task<DateTimeOffset?> GetPullCursorAsync()
    {
        var connection = await database.GetConnectionAsync();
        var row = await connection.FindAsync<SyncStateRow>(PullCursorKey);
        return row is null ? null : DateTimeOffset.Parse(row.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public async Task SetPullCursorAsync(DateTimeOffset cursor)
    {
        var connection = await database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(new SyncStateRow { Key = PullCursorKey, Value = cursor.ToString("O", CultureInfo.InvariantCulture) });
    }
}
