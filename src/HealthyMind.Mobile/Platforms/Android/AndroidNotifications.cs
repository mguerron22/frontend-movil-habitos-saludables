using Android.App;
using Android.Content;
using AndroidX.Core.App;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Notificaciones de Android en tres canales que el usuario puede silenciar por separado en Ajustes:
/// recomendaciones de la IA, recordatorios de hábitos y anuncios.
/// </summary>
internal sealed class AndroidNotifications : IAppNotifications
{
    private const string ChannelId = "recommendations";
    private const string ReminderChannelId = "reminders";
    private const string AnnouncementChannelId = "announcements";
    private const int RecommendationNotificationId = 1;
    private const int AnnouncementNotificationId = 3;

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
        EnsureChannel(ChannelId, "Recomendaciones de la IA", "Avisa cuando la IA analiza tus hábitos y deja una recomendación nueva.");

        // En la pantalla bloqueada solo se ve el aviso genérico: la recomendación habla de los hábitos del usuario.
        var publicVersion = new NotificationCompat.Builder(Context, ChannelId);
        publicVersion.SetSmallIcon(Resource.Drawable.ic_notification);
        publicVersion.SetContentTitle("HealthyMindIA");
        publicVersion.SetContentText("Tienes una nueva recomendación");

        var notification = Build(ChannelId, title, text, OpenInsightsIntent());
        notification.SetPublicVersion(publicVersion.Build());

        // Sin permiso Android descarta el aviso por su cuenta; no hace falta revisar antes.
        NotificationManagerCompat.From(Context)?.Notify(RecommendationNotificationId, notification.Build());
    }

    public void ShowReminder(string reminderId, string title, string text, Guid? habitId = null)
    {
        EnsureChannel(ReminderChannelId, "Recordatorios de hábitos", "Recordatorio diario, racha en riesgo y recordatorios por hábito.");

        var notification = Build(ReminderChannelId, title, text, OpenAppIntent());
        if (habitId is { } id)
        {
            notification.AddAction(0, "✅ Marcar hecho", ActionIntent(ReminderActionReceiver.ActionDone, reminderId, id));
        }

        notification.AddAction(0, "⏰ En 1 hora", ActionIntent(ReminderActionReceiver.ActionSnooze, reminderId, habitId));

        NotificationManagerCompat.From(Context)?.Notify(NotificationIdFor(reminderId), notification.Build());
    }

    public void ShowAnnouncement(string title, string text)
    {
        EnsureChannel(AnnouncementChannelId, "Anuncios", "Avisos generales de HealthyMindIA, como mantenimientos.");
        NotificationManagerCompat.From(Context)?.Notify(AnnouncementNotificationId, Build(AnnouncementChannelId, title, text, OpenAppIntent()).Build());
    }

    public void Dismiss(string reminderId) => NotificationManagerCompat.From(Context)?.Cancel(NotificationIdFor(reminderId));

    /// <summary>Un id estable por recordatorio (10 en adelante), para que los de cada hábito no se pisen.</summary>
    private static int NotificationIdFor(string reminderId)
    {
        var hash = 17;
        foreach (var character in reminderId)
        {
            hash = unchecked((hash * 31) + character);
        }

        return 10 + (int)((uint)hash % 1_000_000);
    }

    private static NotificationCompat.Builder Build(string channel, string title, string text, PendingIntent open)
    {
        var notification = new NotificationCompat.Builder(Context, channel);
        notification.SetSmallIcon(Resource.Drawable.ic_notification);
        notification.SetContentTitle(title);
        notification.SetContentText(text);
        notification.SetStyle(new NotificationCompat.BigTextStyle().BigText(text));
        notification.SetContentIntent(open);
        notification.SetAutoCancel(true);
        notification.SetVisibility(NotificationCompat.VisibilityPrivate);
        return notification;
    }

    private static void EnsureChannel(string id, string name, string description)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var channel = new NotificationChannel(id, name, NotificationImportance.Default) { Description = description };
        ((NotificationManager)Context.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(channel);
    }

    private static PendingIntent ActionIntent(string action, string reminderId, Guid? habitId)
    {
        var intent = new Intent(Context, typeof(ReminderActionReceiver))
            .SetAction(action)
            .SetData(global::Android.Net.Uri.Parse($"healthymind://action/{action}/{reminderId}"))
            .PutExtra(AndroidReminderSchedule.ReminderIdExtra, reminderId);
        if (habitId is { } id)
        {
            intent.PutExtra(ReminderActionReceiver.HabitIdExtra, id.ToString());
        }

        return PendingIntent.GetBroadcast(Context, 2, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    private static PendingIntent OpenAppIntent()
    {
        var intent = new Intent(Context, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(Context, 1, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    private static PendingIntent OpenInsightsIntent()
    {
        var intent = new Intent(Context, typeof(MainActivity))
            .SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop)
            .PutExtra(NotificationLaunch.Extra, NotificationLaunch.Insights);
        return PendingIntent.GetActivity(Context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}

/// <summary>Restricción de batería (Doze y las de cada fabricante).</summary>
internal sealed class AndroidBatteryOptimization : IBatteryOptimization
{
    private static Context Context => Platform.AppContext;

    public bool IsRestricted =>
        Context.GetSystemService(Context.PowerService) is global::Android.OS.PowerManager power
        && !power.IsIgnoringBatteryOptimizations(Context.PackageName);

    public void RequestUnrestricted()
    {
        try
        {
            // Diálogo directo del sistema: "¿Permitir que la app funcione en segundo plano?".
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
                    global::Android.Net.Uri.Parse("package:" + Context.PackageName))
                .AddFlags(ActivityFlags.NewTask);
            Context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // Algunos fabricantes no lo tienen: se abre la lista general de optimización de batería.
            Context.StartActivity(new Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings)
                .AddFlags(ActivityFlags.NewTask));
        }
    }
}
