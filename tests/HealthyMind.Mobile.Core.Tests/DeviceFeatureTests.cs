using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class StepDayTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void First_reading_of_the_day_starts_at_zero() =>
        Assert.Equal(new StepDay(Today, 5000, 0), StepDay.Advance(new StepDay(Today.AddDays(-1), 4000, 3200), Today, 5000));

    [Fact]
    public void Later_readings_add_the_difference()
    {
        var day = StepDay.Advance(new StepDay(Today, 5000, 0), Today, 5800);
        day = StepDay.Advance(day, Today, 6100);

        Assert.Equal(1100, day.Steps);
    }

    [Fact]
    public void After_a_reboot_the_counter_restarts_and_nothing_is_lost()
    {
        var day = StepDay.Advance(new StepDay(Today, 9000, 700), Today, 250);

        Assert.Equal(950, day.Steps);
        Assert.Equal(250, day.LastCounter);
    }

    [Fact]
    public void State_survives_being_saved_as_text()
    {
        var day = new StepDay(Today, 123456, 789);

        Assert.Equal(day, StepDay.Parse(day.Serialize()));
        Assert.Null(StepDay.Parse("basura"));
    }
}

public sealed class StepTrackerTests : LocalDataTest
{
    private readonly FakeStepSensor _sensor = new();
    private readonly FakeSettings _settings = new();
    private readonly HabitService _habits;
    private readonly StepTracker _tracker;

    public StepTrackerTests()
    {
        _habits = new HabitService(Store, Outbox, new QuietSync(), Clock);
        _tracker = new StepTracker(_sensor, _habits, _settings, Clock);
    }

    [Fact]
    public async Task Steps_fill_the_step_habits_of_today()
    {
        var walk = await _habits.SaveAsync(Steps("Caminar", 1000));
        var water = await _habits.SaveAsync(new HabitDraft(null, "Tomar agua", HabitCategory.Hidratacion, 8, "vasos", [], TrackingMode.Manual));

        _sensor.Total = 20_000;
        await _tracker.UpdateAsync();
        _sensor.Total = 21_500;
        var steps = await _tracker.UpdateAsync();

        Assert.Equal(1500, steps);
        var record = await Store.FindRecordAsync(walk.Id, Today);
        Assert.Equal(1500, record!.CompletedValue);
        Assert.True(record.Completed);
        Assert.Null(await Store.FindRecordAsync(water.Id, Today));
    }

    [Fact]
    public async Task A_higher_value_saved_elsewhere_is_never_lowered()
    {
        var walk = await _habits.SaveAsync(Steps("Caminar", 8000));
        await _habits.RecordAsync(walk.Id, Today, 3000);

        _sensor.Total = 100;
        await _tracker.UpdateAsync();
        _sensor.Total = 600;
        await _tracker.UpdateAsync();

        Assert.Equal(3000, (await Store.FindRecordAsync(walk.Id, Today))!.CompletedValue);
    }

    [Fact]
    public async Task Without_sensor_permission_nothing_is_recorded()
    {
        var walk = await _habits.SaveAsync(Steps("Caminar", 1000));
        _sensor.Granted = false;
        _sensor.Total = 5000;

        Assert.Null(await _tracker.UpdateAsync());
        Assert.Null(await Store.FindRecordAsync(walk.Id, Today));
        Assert.Null(_settings.StepCounterState);
    }

    [Fact]
    public async Task Location_is_saved_only_when_the_user_enabled_it()
    {
        var walk = await _habits.SaveAsync(Steps("Caminar", 1000));
        var location = new RecordLocation(new FakeLocation(), _settings);

        await _habits.RecordAsync(walk.Id, Today, 10, await location.CurrentIfEnabledAsync());
        Assert.Null((await Store.FindRecordAsync(walk.Id, Today))!.Latitude);

        Assert.True(await location.SetEnabledAsync(true));
        await _habits.RecordAsync(walk.Id, Today, 20, await location.CurrentIfEnabledAsync());
        var record = await Store.FindRecordAsync(walk.Id, Today);
        Assert.Equal(-0.18065, record!.Latitude);
        Assert.Equal(-78.46783, record.Longitude);
    }

    private static HabitDraft Steps(string name, int goal) =>
        new(null, name, HabitCategory.Ejercicio, goal, "pasos", [], TrackingMode.StepCounter);

    private sealed class FakeStepSensor : IStepSensor
    {
        public long? Total { get; set; }

        public bool Granted { get; set; } = true;

        public bool IsSupported => true;

        public Task<bool> EnsurePermissionAsync() => Task.FromResult(Granted);

        public Task<long?> ReadTotalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Total);
    }

    private sealed class FakeLocation : ILocationProvider
    {
        public Task<bool> EnsurePermissionAsync() => Task.FromResult(true);

        public Task<GeoPoint?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<GeoPoint?>(new GeoPoint(-0.18065, -78.46783));
    }

    private sealed class QuietSync : ISyncService
    {
        public event EventHandler<SyncCompletedEventArgs>? Completed { add { } remove { } }

        public Task<int> PendingCountAsync() => Task.FromResult(0);

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RequestSync()
        {
        }
    }
}
