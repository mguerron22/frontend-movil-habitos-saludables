using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Settings;
using SQLite;

namespace HealthyMind.Mobile.Core.Notifications;

/// <summary>
/// Despertadores del sistema (en Android: AlarmManager), que suenan aunque la app esté cerrada.
/// Cada uno tiene un id ("daily", "streak", "habit:{id}") y al sonar llama a <see cref="Reminders.RunAsync"/>.
/// </summary>
public interface IReminderSchedule
{
    /// <summary>Todos los días a esa hora local; reemplaza el que tenga el mismo id.</summary>
    void ScheduleDaily(string id, TimeOnly at);

    /// <summary>Una sola vez, dentro de <paramref name="delay"/> (para "En 1 hora").</summary>
    void ScheduleOnce(string id, TimeSpan delay);

    void Cancel(string id);
}

/// <summary>Hora de recordatorio de un hábito. Solo vive en este teléfono: no se sincroniza.</summary>
[Table("habit_reminders")]
public sealed class HabitReminderRow
{
    [PrimaryKey]
    public Guid HabitId { get; set; }

    /// <summary>Minutos desde la medianoche (hora local).</summary>
    public int Minutes { get; set; }

    [Ignore]
    public TimeOnly Time => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Minutes));
}

/// <summary>
/// Todos los avisos locales: recordatorio diario, racha en riesgo y recordatorio por hábito.
/// Salen de la base del teléfono, así que funcionan sin conexión.
/// </summary>
public sealed class Reminders(
    IAppSettings settings,
    IReminderSchedule schedule,
    IHabitService habits,
    IAppNotifications notifications,
    LocalDatabase database)
{
    public const string DailyId = "daily";
    public const string StreakId = "streak";
    public const string SnoozePrefix = "snooze:";
    private const string HabitPrefix = "habit:";

    public static readonly TimeOnly DefaultDailyTime = new(20, 0);

    /// <summary>A esta hora todavía da tiempo de salvar la racha.</summary>
    public static readonly TimeOnly StreakTime = new(21, 0);

    public static readonly TimeSpan SnoozeDelay = TimeSpan.FromHours(1);

    /// <summary>Desde cuántos días seguidos vale la pena avisar que la racha se pierde.</summary>
    public const int MinStreakToWarn = 2;

    private const int MaxNamesInText = 3;

    public bool IsDailyEnabled => settings.ReminderEnabled;

    public TimeOnly DailyTime => settings.ReminderTime ?? DefaultDailyTime;

    public bool IsStreakEnabled => settings.StreakAlertEnabled;

    public static string HabitId(Guid habitId) => HabitPrefix + habitId.ToString("N");

    public void EnableDaily(TimeOnly at)
    {
        settings.ReminderEnabled = true;
        settings.ReminderTime = at;
        schedule.ScheduleDaily(DailyId, at);
    }

    public void DisableDaily()
    {
        settings.ReminderEnabled = false;
        schedule.Cancel(DailyId);
    }

    public void SetStreakEnabled(bool enabled)
    {
        settings.StreakAlertEnabled = enabled;
        if (enabled)
        {
            schedule.ScheduleDaily(StreakId, StreakTime);
        }
        else
        {
            schedule.Cancel(StreakId);
        }
    }

    public async Task<TimeOnly?> GetHabitTimeAsync(Guid habitId)
    {
        var connection = await database.GetConnectionAsync();
        var row = await connection.FindAsync<HabitReminderRow>(habitId);
        return row?.Time;
    }

    /// <summary>null apaga el recordatorio del hábito.</summary>
    public async Task SetHabitTimeAsync(Guid habitId, TimeOnly? at)
    {
        var connection = await database.GetConnectionAsync();
        if (at is null)
        {
            await connection.DeleteAsync<HabitReminderRow>(habitId);
            schedule.Cancel(HabitId(habitId));
            return;
        }

        await connection.InsertOrReplaceAsync(new HabitReminderRow { HabitId = habitId, Minutes = (int)at.Value.ToTimeSpan().TotalMinutes });
        schedule.ScheduleDaily(HabitId(habitId), at.Value);
    }

    /// <summary>Al abrir la app o reiniciar el teléfono: el sistema borra las alarmas al apagarse.</summary>
    public async Task RestoreAsync()
    {
        if (IsDailyEnabled)
        {
            schedule.ScheduleDaily(DailyId, DailyTime);
        }

        if (IsStreakEnabled)
        {
            schedule.ScheduleDaily(StreakId, StreakTime);
        }

        var connection = await database.GetConnectionAsync();
        foreach (var row in await connection.Table<HabitReminderRow>().ToListAsync())
        {
            schedule.ScheduleDaily(HabitId(row.HabitId), row.Time);
        }
    }

    /// <summary>"En 1 hora" desde la notificación.</summary>
    public void Snooze(string reminderId) => schedule.ScheduleOnce(SnoozePrefix + reminderId, SnoozeDelay);

    /// <summary>"Marcar hecho" desde la notificación: registra la meta completa de hoy (sin conexión también).</summary>
    public async Task MarkDoneAsync(Guid habitId)
    {
        var habit = await habits.GetAsync(habitId);
        if (habit is not null)
        {
            var today = await habits.GetTodayAsync();
            await habits.RecordAsync(habitId, today.Date, habit.Goal);
        }
    }

    /// <summary>Sonó una alarma: avisa solo si hace falta y si hay una sesión guardada en el teléfono.</summary>
    public async Task RunAsync(string reminderId)
    {
        if (settings.RefreshTokenExpiresAt is null)
        {
            return; // Sesión cerrada: no se recuerda nada a nadie.
        }

        var id = reminderId.StartsWith(SnoozePrefix, StringComparison.Ordinal) ? reminderId[SnoozePrefix.Length..] : reminderId;
        var today = await habits.GetTodayAsync();

        if (id == DailyId)
        {
            if (IsDailyEnabled && PendingText(today) is { } text)
            {
                notifications.ShowReminder(DailyId, "Aún tienes hábitos por hoy", text);
            }
        }
        else if (id == StreakId)
        {
            if (IsStreakEnabled && StreakText(today) is { } text)
            {
                notifications.ShowReminder(StreakId, "¡No pierdas tu racha!", text);
            }
        }
        else if (id.StartsWith(HabitPrefix, StringComparison.Ordinal)
                 && Guid.TryParseExact(id[HabitPrefix.Length..], "N", out var habitId)
                 && await GetHabitTimeAsync(habitId) is not null)
        {
            var item = today.Habits.FirstOrDefault(entry => entry.Habit.Id == habitId);
            if (item is not null && item.Habit.IsActive && !item.IsCompleted)
            {
                notifications.ShowReminder(id, $"Es hora de: {item.Habit.Name}", HabitText(item), habitId);
            }
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

    /// <summary>La racha más larga que se pierde hoy si no se cumple.</summary>
    internal static string? StreakText(TodaySummary today)
    {
        var atRisk = today.Habits
            .Where(item => item.Habit.IsActive && !item.IsCompleted && item.Streak >= MinStreakToWarn)
            .OrderByDescending(item => item.Streak)
            .FirstOrDefault();

        return atRisk is null
            ? null
            : $"Llevas {atRisk.Streak} días seguidos con {atRisk.Habit.Name}. Regístralo hoy para no perder la racha.";
    }

    private static string HabitText(TodayHabit item) =>
        item.Value > 0
            ? $"Vas {item.Value} de {item.Habit.Goal} {item.Habit.Unit}. ¡Tú puedes!"
            : $"Tu meta de hoy: {item.Habit.Goal} {item.Habit.Unit}.";
}
