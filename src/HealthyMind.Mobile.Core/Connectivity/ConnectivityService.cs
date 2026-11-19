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

    /// <summary>Por qué no hay conexión (solo tiene sentido si <see cref="IsOnline"/> es false).</summary>
    OfflineReason Reason { get; }

    event EventHandler<bool>? StatusChanged;

    /// <summary>Lo llama el cliente HTTP cuando una petición no llegó: pasa a modo sin conexión al instante.</summary>
    void ReportServerUnreachable(OfflineReason reason = OfflineReason.ServerDown);

    void ReportServerReachable();

    /// <summary>Consulta /health para confirmar si volvió la conexión.</summary>
    Task<bool> CheckNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>Consulta al servidor si está vivo (GET /health, sin token).</summary>
public interface IServerProbe
{
    /// <summary>null si respondió; si no, el motivo.</summary>
    Task<OfflineReason?> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Motivo de estar sin conexión, para mostrar un mensaje claro.</summary>
public enum OfflineReason
{
    /// <summary>El teléfono no tiene internet.</summary>
    NoNetwork,

    /// <summary>Hay internet pero el servidor no responde.</summary>
    ServerDown,

    /// <summary>El servidor respondió con un certificado que el teléfono no acepta.</summary>
    Certificate,
}

public static class OfflineReasons
{
    /// <summary>Distingue un problema de certificado (SSL) de un servidor caído.</summary>
    public static OfflineReason From(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Security.Authentication.AuthenticationException
                || current.GetType().Name.Contains("Certificate", StringComparison.OrdinalIgnoreCase)
                || current.GetType().Name.Contains("SSL", StringComparison.OrdinalIgnoreCase))
            {
                return OfflineReason.Certificate;
            }
        }

        return OfflineReason.ServerDown;
    }

    public static string Describe(OfflineReason reason) => reason switch
    {
        OfflineReason.NoNetwork => "Sin internet. Tus cambios se guardan en el teléfono y se enviarán al volver.",
        OfflineReason.Certificate => "No se pudo verificar la seguridad del servidor (certificado). Tus cambios se guardan en el teléfono.",
        _ => "El servidor no responde. Tus cambios se guardan en el teléfono y se enviarán al volver.",
    };
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

    [ObservableProperty]
    private OfflineReason _reason = OfflineReason.NoNetwork;

    public ConnectivityService(INetworkStatus network, IServerProbe probe, TimeProvider timeProvider)
    {
        _network = network;
        _probe = probe;
        _isOnline = network.HasNetwork;
        _reason = network.HasNetwork ? OfflineReason.ServerDown : OfflineReason.NoNetwork;
        _network.Changed += OnNetworkChanged;
        _retryTimer = timeProvider.CreateTimer(_ => _ = CheckNowAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<bool>? StatusChanged;

    public void ReportServerUnreachable(OfflineReason reason = OfflineReason.ServerDown) => SetOnline(false, reason);

    public void ReportServerReachable() => SetOnline(true);

    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_network.HasNetwork)
        {
            SetOnline(false, OfflineReason.NoNetwork);
            return false;
        }

        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            var failure = await _probe.CheckAsync(cancellationToken);
            SetOnline(failure is null, failure ?? Reason);
            return failure is null;
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

    private void SetOnline(bool online, OfflineReason? reason = null)
    {
        // Mientras no haya conexión se reintenta solo; con conexión, el timer se detiene.
        _retryTimer.Change(online ? Timeout.InfiniteTimeSpan : RetryInterval, online ? Timeout.InfiniteTimeSpan : RetryInterval);

        var reasonChanged = !online && reason is { } newReason && newReason != Reason;
        if (reasonChanged)
        {
            Reason = reason!.Value;
        }

        if (IsOnline == online && !reasonChanged)
        {
            return;
        }

        IsOnline = online;
        StatusChanged?.Invoke(this, online);
    }
}
