using SQLite;

namespace HealthyMind.Mobile.Core.Data;

/// <summary>
/// Base SQLite del teléfono: lo que hace falta para usar la app sin internet.
/// Cada módulo registra sus tablas en <see cref="LocalSchema"/>; aquí solo se crean y se abre la conexión.
/// </summary>
public sealed class LocalDatabase(string databasePath) : IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    /// <summary>Conexión lista para usar (crea las tablas la primera vez).</summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var connection = new SQLiteAsyncConnection(
                    databasePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);
                await connection.CreateTablesAsync(CreateFlags.None, LocalSchema.Tables.ToArray());
                _connection = connection;
            }

            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Al cerrar sesión: no deben quedar datos del usuario anterior en el teléfono.</summary>
    public async Task ClearAllAsync()
    {
        var connection = await GetConnectionAsync();
        foreach (var table in LocalSchema.Tables)
        {
            await connection.DeleteAllAsync(await connection.GetMappingAsync(table));
        }
    }

    /// <summary>Cierra solo esta conexión (no el pool global de sqlite-net, que comparten otras instancias).</summary>
    public async Task CloseAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
    }

    public void Dispose() => _initLock.Dispose();
}

/// <summary>Tablas locales. Si un módulo necesita guardar algo sin conexión, agrega aquí su tabla.</summary>
public static class LocalSchema
{
    public static IReadOnlyList<Type> Tables { get; } =
    [
        typeof(CachedUserRow),
        typeof(HabitRow),
        typeof(HabitRecordRow),
        typeof(PendingChangeRow),
        typeof(SyncStateRow),
    ];
}

/// <summary>Perfil del último usuario: permite saludar y entrar sin conexión tras la huella.</summary>
[Table("cached_user")]
public sealed class CachedUserRow
{
    /// <summary>Siempre 1: solo hay un usuario por instalación.</summary>
    [PrimaryKey]
    public int Slot { get; set; } = 1;

    public string Json { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }
}
