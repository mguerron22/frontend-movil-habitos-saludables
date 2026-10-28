using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HealthyMind.Mobile.Core;

public static class DependencyInjection
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registra la lógica de la app. La plataforma (MAUI) debe registrar antes: IAppSettings, ISecureTokenStore,
    /// INetworkStatus, IBiometricAuthenticator, INavigationService e IDialogService.
    /// </summary>
    public static IServiceCollection AddHealthyMindCore(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new LocalDatabase(databasePath));
        services.AddSingleton<IUserCache, UserCache>();

        services.AddHttpClient(HealthServerProbe.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(3));
        services.AddSingleton<IServerProbe, HealthServerProbe>();
        services.AddSingleton<ConnectivityService>();
        services.AddSingleton<IConnectivityService>(provider => provider.GetRequiredService<ConnectivityService>());

        services.AddHttpClient<AnonymousApiClient>(client => client.Timeout = RequestTimeout);
        services.AddTransient<AuthenticatedHttpHandler>();
        services.AddHttpClient<AuthenticatedApiClient>(client => client.Timeout = RequestTimeout)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        services.AddTransient<AuthApi>();

        services.AddSingleton<SessionManager>();
        services.AddSingleton<ISessionManager>(provider => provider.GetRequiredService<SessionManager>());
        services.AddSingleton<IAccessTokenSource>(provider => provider.GetRequiredService<SessionManager>());
        services.AddTransient<BiometricEnrollment>();

        services.AddTransient<StartupViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<UnlockViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProfileViewModel>();

        return services;
    }
}
