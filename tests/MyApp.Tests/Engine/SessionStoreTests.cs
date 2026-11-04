using Microsoft.Extensions.Time.Testing;

namespace MyApp.Tests.Engine;

/// <summary>Session expiry with a fake clock (TimeProvider seam). Pruning runs inside Create() when due.</summary>
public class SessionStoreTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    // played?, idle minutes, still there? (limits: 24 h played, 30 min never spun; prune checks at most every 10 min)
    [InlineData(true, 23 * 60, true)]
    [InlineData(true, 25 * 60, false)]
    [InlineData(false, 29, true)]
    [InlineData(false, 31, false)]
    [InlineData(true, 31, true)]
    public void IdleSession_IsKeptOrPruned(bool played, int idleMinutes, bool kept)
    {
        var store = new SessionStore(_clock);
        var session = store.Create();
        session.HasSpun = played;

        _clock.Advance(TimeSpan.FromMinutes(idleMinutes));
        store.Create(); // triggers the due prune (all idle times here are ≥ 10 min)

        Assert.Equal(kept, store.Get(session.Id) is not null);
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

    [Theory]
    [InlineData(true, 20 * 60)]   // seen again after 20 h, then idle 20 h more: 40 h old, 20 h idle
    [InlineData(false, 20)]       // seen again after 20 min, then idle 20 min more
    public void Get_RefreshesLastSeen_AndExtendsTheSessionsLife(bool played, int minutes)
    {
        var store = new SessionStore(_clock);
        var session = store.Create();
        session.HasSpun = played;

        _clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.NotNull(store.Get(session.Id));             // the visit
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.LastSeenUtc);
        _clock.Advance(TimeSpan.FromMinutes(minutes));
        store.Create();                                    // prune is due

        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public void Prune_RunsAtMostEvery10Minutes()
    {
        var store = new SessionStore(_clock);
        var unplayed = store.Create();                     // t = 0

        _clock.Advance(TimeSpan.FromMinutes(25));
        store.Create();                                    // t = 25: prune runs, session 25 min idle → kept
        _clock.Advance(TimeSpan.FromMinutes(6));
        store.Create();                                    // t = 31: expired, but the last prune was 6 min ago

        Assert.NotNull(store.Get(unplayed.Id));            // not pruned yet (this Get also refreshes it)
    }

    [Fact]
    public void Prune_RunsAgainOnce10MinutesHavePassed()
    {
        var store = new SessionStore(_clock);
        var unplayed = store.Create();                     // t = 0

        _clock.Advance(TimeSpan.FromMinutes(25));
        store.Create();                                    // t = 25: prune runs, kept
        _clock.Advance(TimeSpan.FromMinutes(10));
        store.Create();                                    // t = 35: due again → 35 min idle → pruned

        Assert.Null(store.Get(unplayed.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-session")]
    public void Get_UnknownOrMissingId_ReturnsNull(string? id) =>
        Assert.Null(new SessionStore(_clock).Get(id));

    [Fact]
    public void SpinningMarksTheSessionAsPlayed()
    {
        var session = new SessionStore(_clock).Create();
        Assert.False(session.HasSpun);

        new SlotEngine(new CryptoRandomSource()).Apply(session, 1m, Grids.Blank());

        Assert.True(session.HasSpun);
    }
}
