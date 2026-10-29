using HealthyMind.Mobile.Core;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Services;
using HealthyMind.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RemoveNativeEntryUnderline();
        AddPlatformServices(builder.Services);
        builder.Services.AddHealthyMindCore(Path.Combine(FileSystem.AppDataDirectory, "healthymind.db3"));
        AddPages(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// Android dibuja una raya debajo de cada Entry; en el diseño el campo ya va dentro de una caja redondeada.
    /// </summary>
    private static void RemoveNativeEntryUnderline() =>
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SinRaya", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
        });

    /// <summary>Implementaciones de los puertos del Core que dependen de MAUI o de la plataforma.</summary>
    private static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton(Preferences.Default);
        services.AddSingleton(SecureStorage.Default);
        services.AddSingleton(Connectivity.Current);
        services.AddSingleton<IAppSettings, PreferencesAppSettings>();
        services.AddSingleton<ISecureTokenStore, SecureTokenStore>();
        services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<IDialogService, ShellDialogService>();
        services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
        services.AddSingleton(Geolocation.Default);
        services.AddSingleton<ILocationProvider, MauiLocationProvider>();
#if ANDROID
        services.AddSingleton<IBiometricAuthenticator, Platforms.Android.AndroidBiometricAuthenticator>();
        services.AddSingleton<IStepSensor, Platforms.Android.AndroidStepSensor>();
#elif WINDOWS
        services.AddSingleton<IBiometricAuthenticator, Platforms.Windows.WindowsHelloAuthenticator>();
        services.AddSingleton<IStepSensor, NoStepSensor>();
#endif
    }

    private static void AddPages(IServiceCollection services)
    {
        services.AddSingleton<AppShell>();
        services.AddTransient<StartupPage>();
        services.AddTransient<LoginPage>();
        services.AddTransient<RegisterPage>();
        services.AddTransient<UnlockPage>();
        services.AddTransient<HomePage>();
        services.AddTransient<HabitsPage>();
        services.AddTransient<HabitEditorPage>();
        services.AddTransient<StatsPage>();
        services.AddTransient<InsightsPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<EditProfilePage>();
        services.AddTransient<ChangePasswordPage>();
        services.AddTransient<SessionsPage>();
        services.AddTransient<AdminUsersPage>();
    }
}
