using Android.App;
using Android.Content;
using HealthyMind.Mobile.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Despertador del recordatorio diario. Es inexacto a propósito (Android lo junta con otras alarmas para
/// cuidar la batería): llega a la hora elegida o pocos minutos después, sin pedir permisos especiales.
/// </summary>
internal sealed class AndroidReminderSchedule : IReminderSchedule
{
    private static Context Context => Platform.AppContext;

    public void Schedule(TimeOnly at) => ScheduleNext(Context, at);

    public void Cancel() => Alarms(Context).Cancel(AlarmIntent(Context));

    public static void ScheduleNext(Context context, TimeOnly at)
    {
        var now = DateTimeOffset.Now;
        var next = new DateTimeOffset(now.Date + at.ToTimeSpan(), now.Offset);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        Alarms(context).SetAndAllowWhileIdle(AlarmType.RtcWakeup, next.ToUnixTimeMilliseconds(), AlarmIntent(context));
    }

    private static AlarmManager Alarms(Context context) => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    private static PendingIntent AlarmIntent(Context context) =>
        PendingIntent.GetBroadcast(
            context,
            1,
            new Intent(context, typeof(ReminderAlarmReceiver)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}

/// <summary>Suena una vez al día: deja programado el de mañana y avisa si falta algún hábito.</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.ReminderAlarmReceiver", Exported = false)]
internal sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;
        var reminder = services?.GetService<DailyReminder>();
        if (context is null || reminder is null || !reminder.IsEnabled)
        {
            return;
        }

        AndroidReminderSchedule.ScheduleNext(context, reminder.Time);

        // Leer la base local toma un momento: se le pide a Android que espere.
        var pending = GoAsync();
        _ = RunAsync(reminder, services!, pending);
    }

    private static async Task RunAsync(DailyReminder reminder, IServiceProvider services, PendingResult? pending)
    {
        try
        {
            await reminder.RunAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            services.GetService<ILogger<ReminderAlarmReceiver>>()?.LogWarning(exception, "Falló el recordatorio diario.");
        }
        finally
        {
            pending?.Finish();
        }
    }
}

/// <summary>Al reiniciar el teléfono Android borra las alarmas: se vuelven a programar (recordatorio y modo presentación).</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted])]
internal sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // MainApplication.OnCreate ya corrió al arrancar el proceso y restauró todo; esto es por si acaso.
        var services = IPlatformApplication.Current?.Services;
        services?.GetService<DailyReminder>()?.Restore();
        services?.GetService<Core.Notifications.PresentationMode>()?.Restore();
    }
}
