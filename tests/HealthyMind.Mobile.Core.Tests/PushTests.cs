using HealthyMind.Mobile.Core.Data;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class PushTests : LocalDataTest
{
    private readonly FakeSettings _settings = new();
    private readonly ShownNotifications _shown = new();
    private readonly RecommendationStore _store;
    private readonly RecommendationBadge _badge;
    private readonly PushMessageHandler _handler;

    public PushTests()
    {
        _store = new RecommendationStore(Database);
        _badge = new RecommendationBadge(_store, _settings, new IdleSync(), Clock);
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
    public void With_ai_notifications_off_the_push_only_lights_the_bell()
    {
        _settings.AiPushEnabled = false;

        _handler.Handle(Push("Cuida los lunes", Samples.Now));

        Assert.Empty(_shown.Titles);
        Assert.True(_badge.HasUnread);
    }

    [Fact]
    public void Announcement_is_shown_and_does_not_need_a_sync()
    {
        var shouldSync = _handler.Handle(new Dictionary<string, string>
        {
            ["type"] = PushMessageHandler.AnnouncementType,
            ["title"] = "Mantenimiento",
            ["body"] = "Esta noche",
        });

        Assert.False(shouldSync);
        Assert.Equal(["Mantenimiento"], _shown.Titles);
    }

    [Fact]
    public void Opening_the_ai_tab_turns_off_the_bell_and_queues_the_seen_mark()
    {
        _handler.Handle(Push("Cuida los lunes", Samples.Now.AddMinutes(-5)));

        _badge.MarkSeen();

        Assert.False(_badge.HasUnread);
        Assert.NotNull(_settings.SeenSyncPending);
    }

    [Fact]
    public void Unknown_messages_are_ignored()
    {
        Assert.False(_handler.Handle(new Dictionary<string, string> { ["type"] = "otro" }));
        Assert.Empty(_shown.Titles);
    }

    [Fact]
    public async Task Pending_feedback_survives_a_pull_until_it_is_sent()
    {
        var id = Guid.NewGuid();
        await _store.SaveAsync([new RecommendationRow { Id = id, Title = "A", CreatedAt = Samples.Now }]);
        await _store.SetFeedbackAsync(id, helpful: false);

        // Llega la misma recomendación del servidor, todavía sin la opinión.
        await _store.SaveAsync([new RecommendationRow { Id = id, Title = "A", CreatedAt = Samples.Now }]);

        var pending = Assert.Single(await _store.GetPendingFeedbackAsync());
        Assert.False(pending.WasHelpful);

        await _store.MarkFeedbackSentAsync(id);
        Assert.Empty(await _store.GetPendingFeedbackAsync());
    }

    [Fact]
    public void Daily_reminder_lists_only_what_is_missing_today()
    {
        var (water, read, summary) = TwoHabits(readStreak: 1);

        Assert.Equal("Te falta 1 hábito: Tomar agua.", Reminders.PendingText(summary));
        Assert.Null(Reminders.PendingText(summary with { Habits = [summary.Habits[1]] }));
        _ = (water, read);
    }

    [Fact]
    public void Streak_alert_only_for_streaks_at_risk()
    {
        var (_, _, summary) = TwoHabits(readStreak: 1, waterStreak: 5);

        Assert.Equal(
            "Llevas 5 días seguidos con Tomar agua. Regístralo hoy para no perder la racha.",
            Reminders.StreakText(summary));

        var (_, _, noRisk) = TwoHabits(readStreak: 4, waterStreak: 1);
        Assert.Null(Reminders.StreakText(noRisk)); // la racha larga ya está cumplida hoy
    }

    private static (HabitRow Water, HabitRow Read, TodaySummary Summary) TwoHabits(int readStreak, int waterStreak = 0)
    {
        var today = DateOnly.FromDateTime(Samples.Now.UtcDateTime);
        var water = new HabitRow { Id = Guid.NewGuid(), Name = "Tomar agua", IsActive = true, Goal = 8, Unit = "vasos" };
        var read = new HabitRow { Id = Guid.NewGuid(), Name = "Leer", IsActive = true, Goal = 1, Unit = "vez" };
        var summary = new TodaySummary(
            today,
            [
                new TodayHabit(water, null, waterStreak),
                new TodayHabit(read, new HabitRecordRow { HabitId = read.Id, Completed = true }, readStreak),
            ],
            2);
        return (water, read, summary);
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

        public void ShowReminder(string reminderId, string title, string text, Guid? habitId = null) => Titles.Add(text);

        public void ShowAnnouncement(string title, string text) => Titles.Add(title);

        public void Dismiss(string reminderId)
        {
        }
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
