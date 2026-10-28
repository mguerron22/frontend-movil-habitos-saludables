using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Core.Auth;

/// <summary>Entrega un access token vigente al cliente HTTP y lo renueva cuando hace falta.</summary>
public interface IAccessTokenSource
{
    /// <exception cref="SessionExpiredException">No hay sesión o ya no es válida.</exception>
    /// <exception cref="ServerUnreachableException">Hacía falta renovar y no hay conexión.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>El servidor rechazó <paramref name="rejectedToken"/> (401): renueva, salvo que otro ya lo haya hecho.</summary>
    Task<string> RenewAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken);
}

public interface ISessionManager : IAccessTokenSource
{
    /// <summary>Usuario de la sesión actual (viene del servidor o, sin conexión, del perfil guardado).</summary>
    UserProfile? CurrentUser { get; }

    /// <summary>Se entró con huella sin poder hablar con el servidor: se trabaja con los datos locales.</summary>
    bool IsOfflineSession { get; }

    /// <summary>La sesión terminó sin que el usuario la cerrara (venció o suspendieron la cuenta).</summary>
    event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>Se había entrado sin conexión y, al volver el internet, la sesión se renovó con el servidor.</summary>
    event EventHandler? OnlineRestored;

    Task<bool> HasStoredSessionAsync();

    Task LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken = default);

    Task RegisterAsync(NewAccount account, CancellationToken cancellationToken = default);

    /// <summary>Retoma la sesión guardada: es lo que se hace después de la huella.</summary>
    Task<ResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Cierra la sesión y borra los datos locales del usuario.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed record NewAccount(string Username, string Email, string FirstName, string LastName, string Password);

public enum ResumeOutcome
{
    /// <summary>Sesión renovada con el servidor.</summary>
    Online,

    /// <summary>Sin conexión: se entra con el perfil guardado y los datos locales.</summary>
    Offline,

    /// <summary>No hay sesión guardada: pedir contraseña.</summary>
    NoSession,

    /// <summary>La sesión venció o fue cerrada desde otro lado: pedir contraseña.</summary>
    Expired,

    /// <summary>La cuenta está suspendida.</summary>
    Suspended,

    /// <summary>Sin conexión y sin perfil guardado: no hay con qué entrar.</summary>
    Unavailable,
}

public sealed class SessionEndedEventArgs(SessionEndReason reason, string message) : EventArgs
{
    public SessionEndReason Reason { get; } = reason;

    public string Message { get; } = message;
}

public sealed class SessionManager : ISessionManager, IDisposable
{
    public const string ExpiredMessage = "Tu sesión expiró. Ingresa tu contraseña otra vez.";
    public const string SuspendedMessage = "Tu cuenta está suspendida. Comunícate con el administrador.";

    /// <summary>Se renueva un poco antes de que venza, para no mandar un token a punto de morir.</summary>
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(30);

    // Una sola renovación a la vez: el backend toma dos refresh simultáneos con el mismo token como robo.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly AuthApi authApi;
    private readonly ISecureTokenStore tokenStore;
    private readonly IAppSettings settings;
    private readonly IUserCache userCache;
    private readonly LocalDatabase database;
    private readonly IConnectivityService connectivity;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SessionManager> logger;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public SessionManager(
        AuthApi authApi,
        ISecureTokenStore tokenStore,
        IAppSettings settings,
        IUserCache userCache,
        LocalDatabase database,
        IConnectivityService connectivity,
        TimeProvider timeProvider,
        ILogger<SessionManager> logger)
    {
        this.authApi = authApi;
        this.tokenStore = tokenStore;
        this.settings = settings;
        this.userCache = userCache;
        this.database = database;
        this.connectivity = connectivity;
        this.timeProvider = timeProvider;
        this.logger = logger;
        connectivity.StatusChanged += OnConnectivityChanged;
    }

    public UserProfile? CurrentUser { get; private set; }

    public bool IsOfflineSession { get; private set; }

    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public event EventHandler? OnlineRestored;

    public async Task<bool> HasStoredSessionAsync()
    {
        if (settings.RefreshTokenExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow())
        {
            return false;
        }

        return !string.IsNullOrEmpty(await tokenStore.GetRefreshTokenAsync());
    }

    public async Task LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken = default)
    {
        var response = await authApi.LoginAsync(
            new LoginRequest(usernameOrEmail.Trim(), password, settings.DeviceId, settings.DeviceName),
            cancellationToken);
        await StartSessionAsync(response);
    }

    public async Task RegisterAsync(NewAccount account, CancellationToken cancellationToken = default)
    {
        var response = await authApi.RegisterAsync(
            new RegisterRequest(
                account.Username.Trim(),
                account.Email.Trim(),
                account.FirstName.Trim(),
                account.LastName.Trim(),
                account.Password,
                settings.DeviceId,
                settings.DeviceName,
                LocalTimeZone.IanaId()),
            cancellationToken);
        await StartSessionAsync(response);
    }

    public async Task<ResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (!await HasStoredSessionAsync())
        {
            return ResumeOutcome.NoSession;
        }

        // Primero un ping corto a /health: sin servidor se entra sin conexión en segundos, no al vencer la petición.
        if (!await connectivity.CheckNowAsync(cancellationToken))
        {
            return await EnterOfflineAsync();
        }

        try
        {
            await RefreshAsync(rejectedToken: null, cancellationToken);
            return ResumeOutcome.Online;
        }
        catch (ServerUnreachableException)
        {
            return await EnterOfflineAsync();
        }
        catch (SessionExpiredException exception)
        {
            return exception.Reason == SessionEndReason.Suspended ? ResumeOutcome.Suspended : ResumeOutcome.Expired;
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _accessToken;
        if (token is not null && _accessTokenExpiresAt - RenewalMargin > timeProvider.GetUtcNow())
        {
            return token;
        }

        return await RefreshAsync(token, cancellationToken);
    }

    public Task<string> RenewAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken) =>
        RefreshAsync(rejectedToken, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = await tokenStore.GetRefreshTokenAsync();
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                await authApi.LogoutAsync(new LogoutRequest(refreshToken), cancellationToken);
            }
            catch (Exception exception) when (exception is ServerUnreachableException or ApiException)
            {
                // Sin conexión igual se cierra en el teléfono; el token vencerá solo en el servidor.
                logger.LogInformation(exception, "No se pudo avisar el cierre de sesión al servidor.");
            }
        }

        ForgetTokens();
        settings.BiometricEnabled = false;
        settings.BiometricOffered = false;
        await database.ClearAllAsync();
    }

    public void Dispose()
    {
        connectivity.StatusChanged -= OnConnectivityChanged;
        _refreshLock.Dispose();
    }

    private async Task<ResumeOutcome> EnterOfflineAsync()
    {
        var cached = await userCache.GetAsync();
        if (cached is null)
        {
            return ResumeOutcome.Unavailable;
        }

        CurrentUser = cached;
        IsOfflineSession = true;
        return ResumeOutcome.Offline;
    }

    /// <summary>Volvió la conexión estando en sesión sin conexión: se renueva sola, sin pedir nada al usuario.</summary>
    private async void OnConnectivityChanged(object? sender, bool online)
    {
        if (!online || !IsOfflineSession)
        {
            return;
        }

        try
        {
            // Si el access token sigue vigente no se renueva, pero igual se confirma el servidor y se sale del modo sin conexión.
            await RefreshAsync(rejectedToken: null, CancellationToken.None);
            IsOfflineSession = false;
            OnlineRestored?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is ServerUnreachableException or SessionExpiredException or ApiException)
        {
            // Sin servidor se sigue offline; si la sesión murió, SessionEnded ya llevó al login.
            logger.LogInformation(exception, "No se pudo renovar la sesión al volver la conexión.");
        }
    }

    private async Task<string> RefreshAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Mientras esperábamos, otra petición ya renovó: se usa ese token.
            if (_accessToken is not null && _accessToken != rejectedToken
                && _accessTokenExpiresAt - RenewalMargin > timeProvider.GetUtcNow())
            {
                return _accessToken;
            }

            var refreshToken = await tokenStore.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw EndSession(SessionEndReason.Expired);
            }

            AuthResponse response;
            try
            {
                response = await authApi.RefreshAsync(new RefreshRequest(refreshToken, settings.DeviceId), cancellationToken);
            }
            catch (ApiException exception) when (exception.IsUnauthorized || exception.IsForbidden)
            {
                throw EndSession(exception.IsForbidden ? SessionEndReason.Suspended : SessionEndReason.Expired);
            }

            await ApplyAsync(response);
            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task StartSessionAsync(AuthResponse response)
    {
        // Otro usuario en el mismo teléfono: no debe ver los datos locales del anterior.
        var previous = await userCache.GetAsync();
        if (previous is not null && previous.Id != response.User.Id)
        {
            await database.ClearAllAsync();
            settings.BiometricEnabled = false;
            settings.BiometricOffered = false;
        }

        await ApplyAsync(response);
    }

    private async Task ApplyAsync(AuthResponse response)
    {
        await tokenStore.SaveRefreshTokenAsync(response.RefreshToken);
        settings.RefreshTokenExpiresAt = response.RefreshTokenExpiresAt;
        _accessToken = response.AccessToken;
        _accessTokenExpiresAt = response.AccessTokenExpiresAt;
        CurrentUser = response.User;
        IsOfflineSession = false;
        await userCache.SaveAsync(response.User);
    }

    /// <summary>
    /// Olvida los tokens pero conserva los datos locales: si es el mismo usuario, al volver a entrar
    /// recupera lo que tenía pendiente de sincronizar.
    /// </summary>
    private SessionExpiredException EndSession(SessionEndReason reason)
    {
        ForgetTokens();
        var message = reason == SessionEndReason.Suspended ? SuspendedMessage : ExpiredMessage;
        SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason, message));
        return new SessionExpiredException(reason, message);
    }

    private void ForgetTokens()
    {
        tokenStore.Clear();
        settings.RefreshTokenExpiresAt = null;
        _accessToken = null;
        CurrentUser = null;
        IsOfflineSession = false;
    }
}

public static class LocalTimeZone
{
    /// <summary>Zona del teléfono en formato IANA (lo que espera el backend), también en Windows.</summary>
    public static string? IanaId()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
        {
            return local.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;
    }
}
