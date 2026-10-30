using Android.App;
using Android.Content;
using AndroidX.Core.App;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>Notificaciones de Android: un canal "Recomendaciones de la IA" que el usuario puede silenciar en Ajustes.</summary>
internal sealed class AndroidNotifications : IAppNotifications
{
    private const string ChannelId = "recommendations";
    private const int RecommendationNotificationId = 1;

    private static Context Context => Platform.AppContext;

    public async Task<bool> EnsurePermissionAsync()
    {
        // Antes de Android 13 las notificaciones no piden permiso.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        }

        return status == PermissionStatus.Granted;
    }

    public void ShowRecommendation(string title, string text)
    {
        EnsureChannel();

        // En la pantalla bloqueada solo se ve el aviso genérico: la recomendación habla de los hábitos del usuario.
        var publicVersion = new NotificationCompat.Builder(Context, ChannelId);
        publicVersion.SetSmallIcon(Resource.Drawable.ic_notification);
        publicVersion.SetContentTitle("HealthyMindIA");
        publicVersion.SetContentText("Tienes una nueva recomendación");

        var notification = new NotificationCompat.Builder(Context, ChannelId);
        notification.SetSmallIcon(Resource.Drawable.ic_notification);
        notification.SetContentTitle(title);
        notification.SetContentText(text);
        notification.SetStyle(new NotificationCompat.BigTextStyle().BigText(text));
        notification.SetContentIntent(OpenInsightsIntent());
        notification.SetAutoCancel(true);
        notification.SetVisibility(NotificationCompat.VisibilityPrivate);
        notification.SetPublicVersion(publicVersion.Build());

        // Sin permiso Android descarta el aviso por su cuenta; no hace falta revisar antes.
        NotificationManagerCompat.From(Context)?.Notify(RecommendationNotificationId, notification.Build());
    }

    private static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "Recomendaciones de la IA", NotificationImportance.Default)
        {
            Description = "Avisa cuando la IA analiza tus hábitos y deja una recomendación nueva.",
        };
        ((NotificationManager)Context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }

    private static PendingIntent OpenInsightsIntent()
    {
        var intent = new Intent(Context, typeof(MainActivity))
            .SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop)
            .PutExtra(NotificationLaunch.Extra, NotificationLaunch.Insights);
        return PendingIntent.GetActivity(Context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
