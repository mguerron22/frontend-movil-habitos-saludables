using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Core.Sync;

/// <summary>Endpoints de /api/sync.</summary>
public sealed class SyncApi(AuthenticatedApiClient api)
{
    public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<SyncPushResponse>("api/sync/push", request, cancellationToken);

    public Task<SyncPullResponse> PullAsync(DateTimeOffset? since, CancellationToken cancellationToken = default) =>
        api.GetAsync<SyncPullResponse>(
            since is null ? "api/sync/pull" : $"api/sync/pull?since={Uri.EscapeDataString(since.Value.ToString("O"))}",
            cancellationToken);
}

/// <summary>Mantiene el SQLite del teléfono y el servidor de acuerdo (RNF05).</summary>
public interface ISyncService
{
    /// <summary>Terminó una sincronización y pudo haber cambiado lo local: las pantallas recargan.</summary>
    event EventHandler<SyncCompletedEventArgs>? Completed;

    Task<int> PendingCountAsync();

    /// <summary>Envía lo pendiente y descarga lo nuevo. Sin conexión no hace nada y no falla.</summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>Pide sincronizar en segundo plano (tras un cambio local o al volver la conexión).</summary>
    void RequestSync();
}

/// <param name="Rejected">Cambios que el servidor no aceptó, en texto para mostrar al usuario.</param>
public sealed class SyncCompletedEventArgs(IReadOnlyList<string> rejected) : EventArgs
{
    public IReadOnlyList<string> Rejected { get; } = rejected;
}

public sealed class SyncService : ISyncService, IDisposable
{
    /// <summary>El backend acepta hasta 500 elementos de cada tipo por lote.</summary>
    public const int BatchSize = 500;

    private readonly SyncApi _api;
    private readonly IHabitStore _store;
    private readonly IRecommendationStore _recommendations;
    private readonly ISyncOutbox _outbox;
    private readonly IConnectivityService _connectivity;
    private readonly ISessionManager _session;
    private readonly IAppSettings _settings;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _rerunRequested;

    public SyncService(
        SyncApi api,
        IHabitStore store,
        IRecommendationStore recommendations,
        ISyncOutbox outbox,
        IConnectivityService connectivity,
        ISessionManager session,
        IAppSettings settings,
        ILogger<SyncService> logger)
    {
        _api = api;
        _store = store;
        _recommendations = recommendations;
        _outbox = outbox;
        _connectivity = connectivity;
        _session = session;
        _settings = settings;
        _logger = logger;

        // Al volver el internet se envía lo acumulado sin que el usuario haga nada.
        _connectivity.StatusChanged += OnConnectivityChanged;
        _session.OnlineRestored += OnOnlineRestored;
    }

    public event EventHandler<SyncCompletedEventArgs>? Completed;

    public Task<int> PendingCountAsync() => _outbox.CountAsync();

    public void RequestSync() => _ = SyncAsync();

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        // Si ya hay una en curso, se pide otra vuelta al terminar en vez de correr dos a la vez.
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            _rerunRequested = true;
            return;
        }

        try
        {
            do
            {
                _rerunRequested = false;
                if (!CanSync())
                {
                    return;
                }

                await SyncOnceAsync(cancellationToken);
            }
            while (_rerunRequested);
        }
        catch (Exception exception) when (exception is ServerUnreachableException or SessionExpiredException or ApiException)
        {
            // Sin servidor se reintenta al volver la conexión; con la sesión vencida la app ya va al login.
            _logger.LogInformation(exception, "Sincronización interrumpida.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _connectivity.StatusChanged -= OnConnectivityChanged;
        _session.OnlineRestored -= OnOnlineRestored;
        _gate.Dispose();
    }

    private bool CanSync() => _connectivity.IsOnline && _session.CurrentUser is not null && !_session.IsOfflineSession;

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        var push = await PushAsync(cancellationToken);
        await PullAsync(fullSnapshot: push.NeedsFullPull, cancellationToken);
        Completed?.Invoke(this, new SyncCompletedEventArgs(push.Rejected));
    }

    private async Task<PushOutcome> PushAsync(CancellationToken cancellationToken)
    {
        var outcome = new PushOutcome();
        var pending = await _outbox.GetPendingAsync();

        foreach (var batch in pending.Chunk(BatchSize))
        {
            var habits = new List<(HabitRow Row, HabitChange Change)>();
            var records = new List<(HabitRecordRow Row, HabitRecordChange Change)>();

            foreach (var item in batch)
            {
                if (item.Entity == PendingEntity.Habit && await _store.FindHabitAsync(item.EntityId) is { } habit)
                {
                    habits.Add((habit, ToChange(habit)));
                }
                else if (item.Entity == PendingEntity.Record && await _store.FindRecordAsync(item.EntityId) is { } record)
                {
                    records.Add((record, ToChange(record)));
                }
                else
                {
                    await _outbox.RemoveAsync(item.Entity, item.EntityId); // Ya no existe: nada que enviar.
                }
            }

            if (habits.Count == 0 && records.Count == 0)
            {
                continue;
            }

            var response = await _api.PushAsync(
                new SyncPushRequest(_settings.DeviceId, habits.Select(h => h.Change).ToList(), records.Select(r => r.Change).ToList()),
                cancellationToken);

            foreach (var (row, _) in habits)
            {
                var result = response.Habits.FirstOrDefault(r => r.Id == row.Id);
                await ApplyResultAsync(PendingEntity.Habit, row.Id, row.UpdatedAt, row.IsDeleted, row.Name, result, outcome);
            }

            foreach (var (row, _) in records)
            {
                var result = response.Records.FirstOrDefault(r => r.Id == row.Id);
                var habitName = (await _store.FindHabitAsync(row.HabitId))?.Name ?? "Registro";
                await ApplyResultAsync(PendingEntity.Record, row.Id, row.UpdatedAt, row.IsDeleted, $"{habitName} ({row.RecordDate})", result, outcome);
            }
        }

        return outcome;
    }

    private async Task ApplyResultAsync(
        PendingEntity entity,
        Guid id,
        DateTimeOffset sentUpdatedAt,
        bool sentDeleted,
        string label,
        SyncItemResult? result,
        PushOutcome outcome)
    {
        if (result is null)
        {
            return; // El servidor no lo mencionó: queda pendiente para la próxima.
        }

        // Si el usuario lo volvió a cambiar mientras viajaba, ese cambio nuevo sigue pendiente.
        if (await CurrentUpdatedAtAsync(entity, id) is { } current && current != sentUpdatedAt)
        {
            return;
        }

        await _outbox.RemoveAsync(entity, id);

        if (result.Status == SyncItemStatus.Applied && result.ServerId is null)
        {
            if (sentDeleted)
            {
                await RemoveLocalAsync(entity, id);
            }

            return;
        }

        // Rechazado, desactualizado o fusionado con otro id: se trae todo del servidor para quedar iguales.
        outcome.NeedsFullPull = true;
        if (result.Status == SyncItemStatus.Rejected)
        {
            outcome.Rejected.Add($"{label}: {result.Message}");
        }
    }

    private async Task PullAsync(bool fullSnapshot, CancellationToken cancellationToken)
    {
        var since = fullSnapshot ? null : await _outbox.GetPullCursorAsync();
        var response = await _api.PullAsync(since, cancellationToken);
        var pending = (await _outbox.GetPendingAsync()).Select(change => change.Key).ToHashSet();

        foreach (var item in response.Habits)
        {
            if (pending.Contains(PendingChangeRow.KeyFor(PendingEntity.Habit, item.Habit.Id)))
            {
                continue; // Lo local todavía no se envió: no se pisa.
            }

            if (item.IsDeleted)
            {
                await RemoveLocalAsync(PendingEntity.Habit, item.Habit.Id);
            }
            else
            {
                await _store.SaveHabitAsync(ToRow(item.Habit));
            }
        }

        foreach (var item in response.Records)
        {
            if (pending.Contains(PendingChangeRow.KeyFor(PendingEntity.Record, item.Record.Id)))
            {
                continue;
            }

            if (item.IsDeleted)
            {
                await _store.RemoveRecordAsync(item.Record.Id);
            }
            else
            {
                await SaveServerRecordAsync(item.Record);
            }
        }

        if (response.IsFullSnapshot)
        {
            await RemoveMissingAsync(response, pending);
        }

        // Las recomendaciones solo se generan en el servidor: aquí se guardan para leerlas sin conexión.
        await _recommendations.SaveAsync((response.Recommendations ?? []).Select(RecommendationRow.From));

        await _outbox.SetPullCursorAsync(response.ServerTime);
    }

    /// <summary>
    /// Un registro del servidor puede ocupar el mismo día que una fila local con otro id
    /// (el mismo día registrado en otro teléfono): la fila local se reemplaza por la del servidor.
    /// </summary>
    private async Task SaveServerRecordAsync(HabitRecordResponse record)
    {
        var sameDay = await _store.FindRecordAsync(record.HabitId, record.RecordDate);
        if (sameDay is not null && sameDay.Id != record.Id)
        {
            await _store.RemoveRecordAsync(sameDay.Id);
        }

        await _store.SaveRecordAsync(ToRow(record));
    }

    /// <summary>En una descarga completa, lo local que el servidor no tiene (y no está pendiente) sobra.</summary>
    private async Task RemoveMissingAsync(SyncPullResponse response, HashSet<string> pending)
    {
        var habitIds = response.Habits.Select(item => item.Habit.Id).ToHashSet();
        foreach (var habit in await _store.GetAllHabitRowsAsync())
        {
            if (!habitIds.Contains(habit.Id) && !pending.Contains(PendingChangeRow.KeyFor(PendingEntity.Habit, habit.Id)))
            {
                await _store.RemoveHabitAsync(habit.Id);
            }
        }

        var recordIds = response.Records.Select(item => item.Record.Id).ToHashSet();
        foreach (var record in await _store.GetAllRecordRowsAsync())
        {
            if (!recordIds.Contains(record.Id) && !pending.Contains(PendingChangeRow.KeyFor(PendingEntity.Record, record.Id)))
            {
                await _store.RemoveRecordAsync(record.Id);
            }
        }
    }

    private async Task RemoveLocalAsync(PendingEntity entity, Guid id)
    {
        if (entity == PendingEntity.Record)
        {
            await _store.RemoveRecordAsync(id);
            return;
        }

        // Al borrar un hábito, el servidor borra sus registros: aquí igual.
        foreach (var record in await _store.GetRecordsForHabitAsync(id))
        {
            await _store.RemoveRecordAsync(record.Id);
            await _outbox.RemoveAsync(PendingEntity.Record, record.Id);
        }

        await _store.RemoveHabitAsync(id);
    }

    private async Task<DateTimeOffset?> CurrentUpdatedAtAsync(PendingEntity entity, Guid id) => entity == PendingEntity.Habit
        ? (await _store.FindHabitAsync(id))?.UpdatedAt
        : (await _store.FindRecordAsync(id))?.UpdatedAt;

    private void OnConnectivityChanged(object? sender, bool online)
    {
        if (online)
        {
            RequestSync();
        }
    }

    private void OnOnlineRestored(object? sender, EventArgs e) => RequestSync();

    internal static HabitChange ToChange(HabitRow row) => new(
        row.Id,
        row.Name,
        row.Category,
        row.Goal,
        row.Unit,
        row.GetScheduledDays(),
        row.TrackingMode,
        row.IsActive,
        row.IsDeleted,
        row.UpdatedAt);

    internal static HabitRecordChange ToChange(HabitRecordRow row) => new(
        row.Id,
        row.HabitId,
        row.Date,
        row.CompletedValue,
        row.Notes,
        row.Latitude,
        row.Longitude,
        row.IsDeleted,
        row.UpdatedAt);

    internal static HabitRow ToRow(HabitResponse habit)
    {
        var row = new HabitRow
        {
            Id = habit.Id,
            Name = habit.Name,
            Category = habit.Category,
            Goal = habit.Goal,
            Unit = habit.Unit,
            TrackingMode = habit.TrackingMode,
            IsActive = habit.IsActive,
            CreatedAt = habit.CreatedAt,
            UpdatedAt = habit.UpdatedAt,
        };
        row.SetScheduledDays(habit.ScheduledDays);
        return row;
    }

    internal static HabitRecordRow ToRow(HabitRecordResponse record) => new()
    {
        Id = record.Id,
        HabitId = record.HabitId,
        RecordDate = HabitRecordRow.Format(record.RecordDate),
        CompletedValue = record.CompletedValue,
        Completed = record.Completed,
        Notes = record.Notes,
        Latitude = record.Latitude,
        Longitude = record.Longitude,
        UpdatedAt = record.UpdatedAt,
    };

    private sealed class PushOutcome
    {
        public bool NeedsFullPull { get; set; }

        public List<string> Rejected { get; } = [];
    }
}
