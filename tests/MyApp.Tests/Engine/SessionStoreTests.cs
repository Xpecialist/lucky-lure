using Microsoft.Extensions.Time.Testing;

namespace MyApp.Tests.Engine;

/// <summary>Session expiry with a fake clock (TimeProvider seam). Pruning runs inside Create() when due.</summary>
public class SessionStoreTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void PlayedSession_IsPrunedAfter25HoursIdle()
    {
        var store = new SessionStore(_clock);
        var played = store.Create();
        played.HasSpun = true;

        _clock.Advance(TimeSpan.FromHours(25));
        store.Create(); // triggers the due prune

        Assert.Null(store.Get(played.Id));
    }

    [Fact]
    public void NeverSpunSession_IsPrunedAfter31Minutes_WhilePlayedOneSurvives()
    {
        var store = new SessionStore(_clock);
        var played = store.Create();
        played.HasSpun = true;
        var unplayed = store.Create();

        _clock.Advance(TimeSpan.FromMinutes(31));
        store.Create();

        Assert.Null(store.Get(unplayed.Id));
        Assert.NotNull(store.Get(played.Id));
    }
}
