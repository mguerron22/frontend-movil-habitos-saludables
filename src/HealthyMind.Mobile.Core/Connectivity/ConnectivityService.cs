using CommunityToolkit.Mvvm.ComponentModel;

namespace HealthyMind.Mobile.Core.Connectivity;

/// <summary>Estado de red del sistema operativo (en MAUI: Connectivity.Current).</summary>
public interface INetworkStatus
{
    bool HasNetwork { get; }

    event EventHandler? Changed;
}

/// <summary>Responde "¿podemos hablar con el servidor ahora?" y avisa cuando cambia.</summary>
public interface IConnectivityService
{
    bool IsOnline { get; }

    event EventHandler<bool>? StatusChanged;

    /// <summary>Lo llama el cliente HTTP cuando una petición no llegó: pasa a modo sin conexión al instante.</summary>
    void ReportServerUnreachable();

    void ReportServerReachable();

    /// <summary>Consulta /health para confirmar si volvió la conexión.</summary>
    Task<bool> CheckNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>Consulta al servidor si está vivo (GET /health, sin token).</summary>
public interface IServerProbe
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Tener wifi no significa tener servidor: combina la red del sistema con un ping a /health.
/// Mientras está sin conexión vuelve a probar cada cierto tiempo para avisar en cuanto se recupere.
/// </summary>
public sealed partial class ConnectivityService : ObservableObject, IConnectivityService, IDisposable
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    private readonly INetworkStatus _network;
    private readonly IServerProbe _probe;
    private readonly ITimer _retryTimer;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    [ObservableProperty]
    private bool _isOnline = true;

    public ConnectivityService(INetworkStatus network, IServerProbe probe, TimeProvider timeProvider)
    {
        _network = network;
        _probe = probe;
        _isOnline = network.HasNetwork;
        _network.Changed += OnNetworkChanged;
        _retryTimer = timeProvider.CreateTimer(_ => _ = CheckNowAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<bool>? StatusChanged;

    public void ReportServerUnreachable() => SetOnline(false);

    public void ReportServerReachable() => SetOnline(true);

    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_network.HasNetwork)
        {
            SetOnline(false);
            return false;
        }

        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            var reachable = await _probe.IsReachableAsync(cancellationToken);
            SetOnline(reachable);
            return reachable;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public void Dispose()
    {
        _network.Changed -= OnNetworkChanged;
        _retryTimer.Dispose();
        _checkLock.Dispose();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = CheckNowAsync();

    private void SetOnline(bool online)
    {
        // Mientras no haya conexión se reintenta solo; con conexión, el timer se detiene.
        _retryTimer.Change(online ? Timeout.InfiniteTimeSpan : RetryInterval, online ? Timeout.InfiniteTimeSpan : RetryInterval);

        if (IsOnline == online)
        {
            return;
        }

        IsOnline = online;
        StatusChanged?.Invoke(this, online);
    }
}
