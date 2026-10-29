using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Tests;

/// <summary>Base SQLite real en un archivo temporal, como en el teléfono.</summary>
public abstract class LocalDataTest : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hm-tests-{Guid.NewGuid():N}.db3");

    protected LocalDataTest()
    {
        Database = new LocalDatabase(_path);
        Store = new HabitStore(Database);
        Outbox = new SyncOutbox(Database, Clock);
    }

    // Miércoles 28 de octubre de 2026, a mediodía en Ecuador.
    internal ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 28, 17, 0, 0, TimeSpan.Zero));

    internal LocalDatabase Database { get; }

    internal IHabitStore Store { get; }

    internal ISyncOutbox Outbox { get; }

    internal DateOnly Today => DateOnly.FromDateTime(Clock.GetLocalNow().DateTime);

    public void Dispose()
    {
        Database.CloseAsync().GetAwaiter().GetResult();
        Database.Dispose();
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }
}

public sealed class HabitServiceTests : LocalDataTest
{
    private readonly CountingSync _sync = new();
    private readonly HabitService _service;

    public HabitServiceTests()
    {
        _service = new HabitService(Store, Outbox, _sync, Clock);
    }

    private static HabitDraft Water(Guid? id = null, string name = "Tomar agua") =>
        new(id, name, HabitCategory.Hidratacion, 8, "vasos", [], TrackingMode.Manual);

    [Fact]
    public async Task New_habit_is_saved_locally_and_queued_for_the_server()
    {
        var habit = await _service.SaveAsync(Water());

        Assert.Single(await _service.GetHabitsAsync());
        Assert.True(await Outbox.IsPendingAsync(PendingEntity.Habit, habit.Id));
        Assert.Equal(1, _sync.Requests);
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_ignoring_case()
    {
        await _service.SaveAsync(Water());

        var error = await Assert.ThrowsAsync<HabitValidationException>(() => _service.SaveAsync(Water(name: "TOMAR AGUA")));

        Assert.Equal("Ya tienes un hábito con ese nombre.", error.Errors[nameof(HabitDraft.Name)]);
    }

    [Fact]
    public async Task Editing_keeps_the_same_id_and_allows_its_own_name()
    {
        var habit = await _service.SaveAsync(Water());

        var edited = await _service.SaveAsync(Water(habit.Id) with { Goal = 10 });

        Assert.Equal(habit.Id, edited.Id);
        Assert.Equal(10, (await _service.GetAsync(habit.Id))!.Goal);
    }

    [Fact]
    public async Task Record_is_completed_only_when_reaching_the_goal_and_reuses_the_day()
    {
        var habit = await _service.SaveAsync(Water());

        var partial = await _service.RecordAsync(habit.Id, Today, 5);
        var full = await _service.RecordAsync(habit.Id, Today, 8);

        Assert.False(partial.Completed);
        Assert.True(full.Completed);
        Assert.Equal(partial.Id, full.Id);
    }

    [Fact]
    public async Task Paused_habit_cannot_start_a_new_day()
    {
        var habit = await _service.SaveAsync(Water());
        await _service.SetActiveAsync(habit.Id, false);

        var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.RecordAsync(habit.Id, Today, 8));

        Assert.Contains("pausado", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Future_dates_are_rejected()
    {
        var habit = await _service.SaveAsync(Water());

        await Assert.ThrowsAsync<UserMessageException>(() => _service.RecordAsync(habit.Id, Today.AddDays(1), 8));
    }

    [Fact]
    public async Task Deleting_hides_the_habit_drops_its_records_and_queues_only_the_habit()
    {
        var habit = await _service.SaveAsync(Water());
        var record = await _service.RecordAsync(habit.Id, Today, 8);

        await _service.DeleteAsync(habit.Id);

        Assert.Empty(await _service.GetHabitsAsync());
        Assert.Null(await Store.FindRecordAsync(record.Id));
        Assert.True(await Outbox.IsPendingAsync(PendingEntity.Habit, habit.Id));
        Assert.False(await Outbox.IsPendingAsync(PendingEntity.Record, record.Id));
    }

    [Fact]
    public async Task Today_lists_only_scheduled_active_habits_pending_first()
    {
        var water = await _service.SaveAsync(Water());
        await _service.SaveAsync(Water(name: "Leer") with { Category = HabitCategory.Lectura });
        var paused = await _service.SaveAsync(Water(name: "Meditar"));
        await _service.SetActiveAsync(paused.Id, false);
        await _service.SaveAsync(Water(name: "Solo domingos") with { Days = [DayOfWeek.Sunday] });
        await _service.RecordAsync(water.Id, Today, 8);

        var today = await _service.GetTodayAsync();

        Assert.Equal(["Leer", "Tomar agua"], today.Habits.Select(item => item.Habit.Name));
        Assert.Equal(1, today.Completed);
        Assert.Equal(0.5, today.Progress);
        Assert.Equal(3, today.ActiveHabits);
    }

    private sealed class CountingSync : ISyncService
    {
        public int Requests { get; private set; }

        public event EventHandler<SyncCompletedEventArgs>? Completed;

        public Task<int> PendingCountAsync() => Task.FromResult(0);

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RequestSync()
        {
            Requests++;
            Completed?.Invoke(this, new SyncCompletedEventArgs([]));
        }
    }
}

public sealed class HabitInsightsTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 28);

    private static HabitRecordRow Done(Guid habitId, DateOnly day) =>
        new() { Id = Guid.NewGuid(), HabitId = habitId, RecordDate = HabitRecordRow.Format(day), Completed = true };

    [Fact]
    public void Streak_counts_consecutive_completed_days_and_today_does_not_break_it()
    {
        var habit = new HabitRow { Id = Guid.NewGuid() };
        var records = new[] { Done(habit.Id, Wednesday.AddDays(-1)), Done(habit.Id, Wednesday.AddDays(-2)), Done(habit.Id, Wednesday.AddDays(-4)) };

        Assert.Equal(2, HabitInsights.Streak(habit, records, Wednesday));
        Assert.Equal(3, HabitInsights.Streak(habit, [.. records, Done(habit.Id, Wednesday)], Wednesday));
    }

    [Fact]
    public void Days_the_habit_does_not_apply_do_not_break_the_streak()
    {
        // Solo lunes, miércoles y viernes: el martes sin registro no corta.
        var habit = new HabitRow { Id = Guid.NewGuid() };
        habit.SetScheduledDays([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);
        var records = new[] { Done(habit.Id, Wednesday), Done(habit.Id, Wednesday.AddDays(-2)), Done(habit.Id, Wednesday.AddDays(-5)) };

        Assert.Equal(3, HabitInsights.Streak(habit, records, Wednesday));
    }

    [Fact]
    public void Seven_days_are_stored_as_everyday()
    {
        var habit = new HabitRow();
        habit.SetScheduledDays(Enum.GetValues<DayOfWeek>());

        Assert.Empty(habit.GetScheduledDays());
        Assert.True(habit.IsScheduledOn(Wednesday));
    }
}

public sealed class HabitFormatTests
{
    [Fact]
    public void Days_are_written_like_people_say_them()
    {
        Assert.Equal("Todos los días", Core.ViewModels.HabitFormat.Days([]));
        Assert.Equal("Lunes a viernes", Core.ViewModels.HabitFormat.Days([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]));
        Assert.Equal("L · X · D", Core.ViewModels.HabitFormat.Days([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday]));
    }

    [Theory]
    [InlineData(1, "1 pausado")]
    [InlineData(2, "2 pausados")]
    [InlineData(0, "0 pausados")]
    public void Counts_use_singular_only_for_one(int count, string expected) =>
        Assert.Equal(expected, Core.ViewModels.HabitFormat.Count(count, "pausado", "pausados"));
}
