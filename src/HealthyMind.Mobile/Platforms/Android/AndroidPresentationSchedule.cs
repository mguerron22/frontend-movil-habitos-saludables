using Android.App;
using Android.Content;
using Android.OS;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Despertador del modo presentación: una alarma que se vuelve a poner sola cada vez que suena.
/// Con el permiso de alarmas exactas llega a los 3 minutos justos; sin él, Android puede atrasarla.
/// </summary>
internal sealed class AndroidPresentationSchedule : IPresentationSchedule
{
    private static Context Context => Platform.AppContext;

    public bool IsPrecise => CanBeExact(Context);

    public void Start() => ScheduleNext(Context);

    public void Stop() => Alarms(Context).Cancel(AlarmIntent(Context));

    public void OpenPrecisionSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return;
        }

        var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm,
                global::Android.Net.Uri.Parse("package:" + Context.PackageName))
            .AddFlags(ActivityFlags.NewTask);
        Context.StartActivity(intent);
    }

    /// <summary>Programa la próxima vuelta; reemplaza la anterior si ya había una.</summary>
    public static void ScheduleNext(Context context)
    {
        var triggerAt = SystemClock.ElapsedRealtime() + (long)Core.Notifications.PresentationMode.Interval.TotalMilliseconds;
        var alarms = Alarms(context);
        if (CanBeExact(context))
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, AlarmIntent(context));
        }
        else
        {
            alarms.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, AlarmIntent(context));
        }
    }

    private static bool CanBeExact(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) || Alarms(context).CanScheduleExactAlarms();

    private static AlarmManager Alarms(Context context) => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    private static PendingIntent AlarmIntent(Context context) =>
        PendingIntent.GetBroadcast(
            context,
            0,
            new Intent(context, typeof(PresentationAlarmReceiver)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}

/// <summary>Suena cada 3 minutos: deja programada la siguiente y lanza la revisión que pide la recomendación.</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.PresentationAlarmReceiver", Exported = false)]
internal sealed class PresentationAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var settings = IPlatformApplication.Current?.Services.GetService<IAppSettings>();
        if (context is null || settings?.PresentationMode != true)
        {
            return; // Lo apagaron: no se vuelve a programar.
        }

        AndroidPresentationSchedule.ScheduleNext(context);
        RecommendationCheckJob.RunPresentationCheck(context);
    }
}
