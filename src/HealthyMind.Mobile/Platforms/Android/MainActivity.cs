using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        NotificationLaunch.Request(Intent?.GetStringExtra(NotificationLaunch.Extra));
    }

    /// <summary>Se tocó una notificación con la app ya abierta (LaunchMode SingleTop).</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        NotificationLaunch.Request(intent?.GetStringExtra(NotificationLaunch.Extra));
    }
}
