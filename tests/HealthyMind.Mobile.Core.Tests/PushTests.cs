using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class PushTests : LocalDataTest
{
    private readonly FakeSettings _settings = new();
    private readonly ShownNotifications _shown = new();
    private readonly RecommendationBadge _badge;
    private readonly PushMessageHandler _handler;

    public PushTests()
    {
        _badge = new RecommendationBadge(new RecommendationStore(Database), _settings, new IdleSync(), Clock);
        _handler = new PushMessageHandler(_settings, _shown, _badge);
    }

    [Fact]
    public void Push_shows_the_recommendation_once_and_lights_the_bell()
    {
        var data = Push("Cuida los lunes", Samples.Now);

        Assert.True(_handler.Handle(data));
        Assert.True(_handler.Handle(data)); // Firebase puede repetirlo: no se avisa dos veces.

        Assert.Equal(["Cuida los lunes"], _shown.Titles);
        Assert.Equal(Samples.Now, _settings.LastNotifiedRecommendationAt);
        Assert.True(_badge.HasUnread);
    }

    [Fact]
    public void Opening_the_ai_tab_turns_off_the_bell()
    {
        _handler.Handle(Push("Cuida los lunes", Samples.Now.AddMinutes(-5)));

        _badge.MarkSeen();

        Assert.False(_badge.HasUnread);
    }

    [Fact]
    public void Unknown_messages_are_ignored()
    {
        Assert.False(_handler.Handle(new Dictionary<string, string> { ["type"] = "otro" }));
        Assert.Empty(_shown.Titles);
    }

    [Fact]
    public void Reminder_lists_only_what_is_missing_today()
    {
        var today = DateOnly.FromDateTime(Samples.Now.UtcDateTime);
        HabitRow Habit(string name) => new() { Id = Guid.NewGuid(), Name = name, IsActive = true };
        var water = Habit("Tomar agua");
        var read = Habit("Leer");
        var summary = new TodaySummary(
            today,
            [
                new TodayHabit(water, null, 0),
                new TodayHabit(read, new HabitRecordRow { HabitId = read.Id, Completed = true }, 1),
            ],
            2);

        Assert.Equal("Te falta 1 hábito: Tomar agua.", DailyReminder.PendingText(summary));
        Assert.Null(DailyReminder.PendingText(summary with { Habits = [summary.Habits[1]] }));
    }

    private static Dictionary<string, string> Push(string title, DateTimeOffset createdAt) => new()
    {
        ["type"] = PushMessageHandler.RecommendationType,
        ["title"] = title,
        ["summary"] = "Resumen",
        ["createdAt"] = createdAt.ToString("O"),
    };

    private sealed class ShownNotifications : IAppNotifications
    {
        public List<string> Titles { get; } = [];

        public Task<bool> EnsurePermissionAsync() => Task.FromResult(true);

        public void ShowRecommendation(string title, string text) => Titles.Add(title);

        public void ShowReminder(string title, string text) => Titles.Add(text);
    }

    private sealed class IdleSync : ISyncService
    {
        public event EventHandler<SyncCompletedEventArgs>? Completed { add { } remove { } }

        public Task<int> PendingCountAsync() => Task.FromResult(0);

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RequestSync()
        {
        }
    }
}
