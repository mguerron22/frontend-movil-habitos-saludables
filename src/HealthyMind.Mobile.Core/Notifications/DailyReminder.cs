using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>El despertador diario del recordatorio (en Android: AlarmManager, sobrevive con la app cerrada).</summary>
public interface IReminderSchedule
{
    /// <summary>Programa el próximo aviso a esa hora local; reemplaza el anterior.</summary>
    void Schedule(TimeOnly at);

    void Cancel();
}

/// <summary>
/// Recordatorio diario a la hora que elige el usuario: "Te faltan 2 hábitos hoy: Tomar agua, Leer".
/// Todo sale de la base local, así que funciona sin conexión.
/// </summary>
public sealed class DailyReminder(IAppSettings settings, IReminderSchedule schedule, IHabitService habits, IAppNotifications notifications)
{
    public static readonly TimeOnly DefaultTime = new(20, 0);

    private const int MaxNamesInText = 3;

    public bool IsEnabled => settings.ReminderEnabled;

    public TimeOnly Time => settings.ReminderTime ?? DefaultTime;

    public void Enable(TimeOnly at)
    {
        settings.ReminderEnabled = true;
        settings.ReminderTime = at;
        schedule.Schedule(at);
    }

    public void Disable()
    {
        settings.ReminderEnabled = false;
        schedule.Cancel();
    }

    /// <summary>Al abrir la app o reiniciar el teléfono: el sistema borra las alarmas al apagarse.</summary>
    public void Restore()
    {
        if (IsEnabled)
        {
            schedule.Schedule(Time);
        }
    }

    /// <summary>Sonó la alarma: avisa solo si falta algo de hoy y hay una sesión guardada.</summary>
    public async Task RunAsync()
    {
        if (!IsEnabled || settings.RefreshTokenExpiresAt is null)
        {
            return;
        }

        var text = PendingText(await habits.GetTodayAsync());
        if (text is not null)
        {
            notifications.ShowReminder("Aún tienes hábitos por hoy", text);
        }
    }

    internal static string? PendingText(TodaySummary today)
    {
        var pending = today.Habits
            .Where(item => item.Habit.IsActive && !item.IsCompleted)
            .Select(item => item.Habit.Name)
            .ToList();

        if (pending.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", pending.Take(MaxNamesInText));
        if (pending.Count > MaxNamesInText)
        {
            names += $" y {pending.Count - MaxNamesInText} más";
        }

        return pending.Count == 1 ? $"Te falta 1 hábito: {names}." : $"Te faltan {pending.Count} hábitos: {names}.";
    }
}
