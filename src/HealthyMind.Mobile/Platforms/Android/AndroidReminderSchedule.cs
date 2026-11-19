using Android.App;
using Android.Content;
using Android.OS;
using HealthyMind.Mobile.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Despertadores de los recordatorios (diario, racha, por hábito y "En 1 hora"). Son inexactos a propósito:
/// Android los junta con otras alarmas para cuidar la batería, así que llegan a la hora o pocos minutos
/// después, sin pedir permisos especiales. Cada id tiene su propia alarma (va en el Data del Intent).
/// </summary>
internal sealed class AndroidReminderSchedule : IReminderSchedule
{
    internal const string ReminderIdExtra = "healthymind.reminder";

    private static Context Context => Platform.AppContext;

    public void ScheduleDaily(string id, TimeOnly at) => ScheduleDailyAlarm(Context, id, at);

    public void ScheduleOnce(string id, TimeSpan delay) =>
        Alarms(Context).SetAndAllowWhileIdle(
            AlarmType.ElapsedRealtimeWakeup,
            SystemClock.ElapsedRealtime() + (long)delay.TotalMilliseconds,
            AlarmIntent(Context, id));

    public void Cancel(string id) => Alarms(Context).Cancel(AlarmIntent(Context, id));

    internal static void ScheduleDailyAlarm(Context context, string id, TimeOnly at)
    {
        var now = DateTimeOffset.Now;
        var next = new DateTimeOffset(now.Date + at.ToTimeSpan(), now.Offset);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        Alarms(context).SetAndAllowWhileIdle(AlarmType.RtcWakeup, next.ToUnixTimeMilliseconds(), AlarmIntent(context, id));
    }

    private static AlarmManager Alarms(Context context) => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    private static PendingIntent AlarmIntent(Context context, string id)
    {
        // El Data hace único al PendingIntent: cada recordatorio tiene su alarma y se cancela por separado.
        var intent = new Intent(context, typeof(ReminderAlarmReceiver))
            .SetData(global::Android.Net.Uri.Parse("healthymind://reminder/" + id))
            .PutExtra(ReminderIdExtra, id);
        return PendingIntent.GetBroadcast(context, 1, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}

/// <summary>Suena un recordatorio: si es diario deja programado el de mañana y revisa si hace falta avisar.</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.ReminderAlarmReceiver", Exported = false)]
internal sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;
        var reminders = services?.GetService<Reminders>();
        var id = intent?.GetStringExtra(AndroidReminderSchedule.ReminderIdExtra);
        if (context is null || reminders is null || string.IsNullOrEmpty(id))
        {
            return;
        }

        // Leer la base local toma un momento: se le pide a Android que espere.
        var pending = GoAsync();
        _ = BackgroundWork.RunAsync(services!, pending, async () =>
        {
            if (!id.StartsWith(Reminders.SnoozePrefix, StringComparison.Ordinal))
            {
                await reminders.RestoreAsync(); // Reprograma los diarios para mañana.
            }

            await reminders.RunAsync(id);
        });
    }
}

/// <summary>Botones del aviso: "Marcar hecho" registra el hábito sin abrir la app; "En 1 hora" lo pospone.</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.ReminderActionReceiver", Exported = false)]
internal sealed class ReminderActionReceiver : BroadcastReceiver
{
    internal const string ActionDone = "ec.edu.uisrael.healthymind.REMINDER_DONE";
    internal const string ActionSnooze = "ec.edu.uisrael.healthymind.REMINDER_SNOOZE";
    internal const string HabitIdExtra = "healthymind.habit";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;
        var reminders = services?.GetService<Reminders>();
        var notifications = services?.GetService<IAppNotifications>();
        var id = intent?.GetStringExtra(AndroidReminderSchedule.ReminderIdExtra);
        if (reminders is null || notifications is null || string.IsNullOrEmpty(id))
        {
            return;
        }

        notifications.Dismiss(id);
        if (intent!.Action == ActionSnooze)
        {
            reminders.Snooze(id);
            return;
        }

        if (intent.Action == ActionDone && Guid.TryParse(intent.GetStringExtra(HabitIdExtra), out var habitId))
        {
            var pending = GoAsync();
            _ = BackgroundWork.RunAsync(services!, pending, () => reminders.MarkDoneAsync(habitId));
        }
    }
}

/// <summary>Al reiniciar el teléfono Android borra las alarmas: se vuelven a programar todas.</summary>
[BroadcastReceiver(Name = "ec.edu.uisrael.healthymind.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted])]
internal sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null)
        {
            return;
        }

        services.GetService<Core.Notifications.PresentationMode>()?.Restore();
        var reminders = services.GetService<Reminders>();
        if (reminders is not null)
        {
            var pending = GoAsync();
            _ = BackgroundWork.RunAsync(services, pending, reminders.RestoreAsync);
        }
    }
}

internal static class BackgroundWork
{
    /// <summary>Corre el trabajo de un receptor sin dejar que un error cierre la app, y avisa a Android al terminar.</summary>
    public static async Task RunAsync(IServiceProvider services, BroadcastReceiver.PendingResult? pending, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            services.GetService<ILogger<ReminderAlarmReceiver>>()?.LogWarning(exception, "Falló un recordatorio en segundo plano.");
        }
        finally
        {
            pending?.Finish();
        }
    }
}
