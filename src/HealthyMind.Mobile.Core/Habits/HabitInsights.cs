using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.Habits;

/// <summary>Un hábito en la lista de hoy.</summary>
public sealed record TodayHabit(HabitRow Habit, HabitRecordRow? Record, int Streak)
{
    public bool IsCompleted => Record?.Completed == true;

    public int Value => Record?.CompletedValue ?? 0;
}

/// <summary>Resumen de hoy para la tarjeta "Energía del día".</summary>
public sealed record TodaySummary(DateOnly Date, IReadOnlyList<TodayHabit> Habits, int ActiveHabits)
{
    public int Expected => Habits.Count;

    public int Completed => Habits.Count(habit => habit.IsCompleted);

    /// <summary>De 0 a 1.</summary>
    public double Progress => Expected == 0 ? 0 : (double)Completed / Expected;
}

/// <summary>Cálculos sobre los datos locales: así el inicio funciona aunque no haya internet.</summary>
public static class HabitInsights
{
    public static TodaySummary Today(IReadOnlyList<HabitRow> habits, IReadOnlyList<HabitRecordRow> records, DateOnly today)
    {
        var byHabit = records.GroupBy(record => record.HabitId).ToDictionary(group => group.Key, group => group.ToList());
        var active = habits.Where(habit => habit.IsActive).ToList();

        var todays = active
            .Where(habit => habit.IsScheduledOn(today))
            .Select(habit =>
            {
                var own = byHabit.GetValueOrDefault(habit.Id) ?? [];
                var todayRecord = own.FirstOrDefault(record => record.Date == today);
                return new TodayHabit(habit, todayRecord, Streak(habit, own, today));
            })
            // Primero lo pendiente, como en una lista de tareas; lo cumplido baja.
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.Habit.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new TodaySummary(today, todays, active.Count);
    }

    /// <summary>
    /// Días programados seguidos con la meta cumplida. Los días que el hábito no toca no cortan la racha,
    /// y hoy no la corta mientras no termine.
    /// </summary>
    public static int Streak(HabitRow habit, IReadOnlyCollection<HabitRecordRow> records, DateOnly today)
    {
        var completed = records.Where(record => record.Completed).Select(record => record.Date).ToHashSet();
        var streak = 0;

        for (var day = today; day > today.AddDays(-366); day = day.AddDays(-1))
        {
            if (!habit.IsScheduledOn(day))
            {
                continue;
            }

            if (completed.Contains(day))
            {
                streak++;
            }
            else if (day != today)
            {
                break;
            }
        }

        return streak;
    }

    public static string Motivation(TodaySummary summary) => summary switch
    {
        { ActiveHabits: 0 } => "Crea tu primer hábito y empieza a sumar bienestar.",
        { Expected: 0 } => "Hoy no tienes hábitos programados. ¡Disfruta tu descanso!",
        { Progress: >= 1 } => "¡Día completo! Tu constancia está dando frutos.",
        { Progress: >= 0.5 } => "¡Vas a un gran ritmo! Casi alcanzas el equilibrio completo hoy.",
        { Completed: > 0 } => "Buen comienzo. Cada hábito cumplido suma.",
        _ => "Empieza por el más sencillo: un paso a la vez.",
    };
}

/// <summary>Reglas del formulario, iguales a las del backend.</summary>
public static class HabitRules
{
    public const int NameMaxLength = 100;
    public const int UnitMaxLength = 30;
    public const int MaxGoal = 1_000_000;
    public const int MaxRecordValue = 10_000_000;

    public static IReadOnlyDictionary<string, string> Validate(HabitDraft draft, IEnumerable<string> otherNames)
    {
        var errors = new Dictionary<string, string>();
        var name = draft.Name.Trim();

        if (name.Length == 0)
        {
            errors[nameof(HabitDraft.Name)] = "Escribe un nombre para el hábito.";
        }
        else if (name.Length > NameMaxLength)
        {
            errors[nameof(HabitDraft.Name)] = $"Máximo {NameMaxLength} caracteres.";
        }
        else if (otherNames.Any(other => string.Equals(other.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            errors[nameof(HabitDraft.Name)] = "Ya tienes un hábito con ese nombre.";
        }

        if (draft.Goal is < 1 or > MaxGoal)
        {
            errors[nameof(HabitDraft.Goal)] = "La meta debe ser un número mayor que cero.";
        }

        var unit = draft.Unit.Trim();
        if (unit.Length == 0)
        {
            errors[nameof(HabitDraft.Unit)] = "Escribe la unidad (vasos, minutos, páginas...).";
        }
        else if (unit.Length > UnitMaxLength)
        {
            errors[nameof(HabitDraft.Unit)] = $"Máximo {UnitMaxLength} caracteres.";
        }

        if (!Enum.IsDefined(draft.Category))
        {
            errors[nameof(HabitDraft.Category)] = "Elige una categoría.";
        }

        return errors;
    }
}

/// <summary>Cómo se muestra cada categoría (nombre, ícono y colores del mock).</summary>
/// <param name="ChartColor">Color de la categoría en los gráficos (dona de distribución).</param>
public sealed record CategoryInfo(
    HabitCategory Category,
    string Name,
    string Icon,
    string TileColor,
    string ChipColor,
    string ChipTextColor,
    string ChartColor)
{
    public static readonly IReadOnlyList<CategoryInfo> All =
    [
        new(HabitCategory.Salud, "Salud", "cat_salud.png", "#FDE4E1", "#FDE4E1", "#B42318", "#5FE3CC"),
        new(HabitCategory.Ejercicio, "Ejercicio", "cat_ejercicio.png", "#FFE3D3", "#FFE3D3", "#C2410C", "#2EC4B6"),
        new(HabitCategory.Nutricion, "Nutrición", "cat_nutricion.png", "#E5F6D8", "#E5F6D8", "#3F7D20", "#0E6B5E"),
        new(HabitCategory.Lectura, "Lectura", "cat_lectura.png", "#E3F7F2", "#FFE3D3", "#C2410C", "#B9C4C1"),
        new(HabitCategory.Productividad, "Productividad", "cat_productividad.png", "#E6E9FB", "#E6E9FB", "#3F4BB5", "#7C86E0"),
        new(HabitCategory.Bienestar, "Bienestar", "cat_bienestar.png", "#DDF7F1", "#FBEDE9", "#56625F", "#F6B48A"),
        new(HabitCategory.Hidratacion, "Hidratación", "cat_hidratacion.png", "#DDF7F1", "#FBEDE9", "#56625F", "#7DD3FC"),
    ];

    public static CategoryInfo For(HabitCategory category) => All.First(info => info.Category == category);
}
