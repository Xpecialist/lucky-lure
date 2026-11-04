namespace MyApp.Tests.Engine;

/// <summary>
/// Free-spin collect and the retrigger meter, exactly (the random-spin versions are in FeatureTests).
/// Grids here have Wilds only on reels 3-5 and the blank pattern on reels 1-2, so no line ever pays
/// and every win is the collect.
/// </summary>
public class CollectRetriggerTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply
    private readonly int[][] _paylines;

    public CollectRetriggerTests() => _paylines = _engine.Info().Paylines;

    /// <summary>Fish 2 (reel 1 top) and Fish 5 (reel 1 middle) = 7 × bet, plus <paramref name="wilds"/> Wilds (max 9).</summary>
    private static Cell[][] WildGrid(int wilds, decimal bet = 1m)
    {
        var grid = Grids.Parse(bet, "F2 Q J Q J", "F5 FL R FL R", Grids.BlankBottom);
        var spots = Enumerable.Range(2, 3).SelectMany(reel => Enumerable.Range(0, 3).Select(row => (reel, row)));
        return Grids.With(grid, new Cell(Symbol.Wild, null), [.. spots.Take(wilds)]);
    }

    /// <summary>A session mid-feature with a given meter (multiplier follows the retriggers so far).</summary>
    private static GameSession Feature(int spinsLeft, int wildsCollected, int retriggers, decimal bet = 1m)
    {
        var s = Play.InFeature(spinsLeft, bet);
        (s.WildsCollected, s.Retriggers, s.Multiplier) = (wildsCollected, retriggers, Contract.Multipliers[retriggers]);
        return s;
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 2)]    // multiplier ×2
    [InlineData(3, 2, 3)]    // multiplier ×3, no retrigger (8 + 3 = 11 < 12)
    [InlineData(1, 3, 10)]   // multiplier ×10, meter already full
    public void Collect_IsFishSumTimesWildsTimesMultiplier(int wilds, int retriggers, int multiplier)
    {
        var session = Feature(spinsLeft: 5, wildsCollected: 4 * retriggers, retriggers);
        var before = _engine.Snapshot(session);

        var r = _engine.Apply(session, null, WildGrid(wilds));

        Assert.Empty(r.LineWins);
        Assert.Equal(wilds, r.WildCount);
        Assert.Equal(7m * wilds * multiplier, r.CollectWin);
        Assert.Equal(r.CollectWin, r.TotalWin);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.Money(r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void NoWilds_NoCollect_EvenWithFishOnScreen()
    {
        var r = _engine.Apply(Feature(5, 0, 0), null, WildGrid(wilds: 0));

        Assert.Equal((0, 0m, 0m), (r.WildCount, r.CollectWin, r.TotalWin));
    }

    [Theory]
    // collected before, retriggers before, wilds landing → retriggers after, spins added
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(2, 0, 1, 0, 0)]     // 3 of 4: not yet
    [InlineData(3, 0, 1, 1, 10)]    // exactly the threshold
    [InlineData(3, 0, 5, 2, 20)]    // 3 → 8 crosses two thresholds in one spin
    [InlineData(7, 1, 1, 2, 10)]
    [InlineData(11, 2, 1, 3, 10)]   // the third (last) retrigger
    [InlineData(8, 2, 8, 3, 10)]    // 8 → 16 would be four thresholds: capped at three
    [InlineData(12, 3, 5, 3, 0)]    // already capped: the meter fills but nothing happens
    public void RetriggerMeter_StepsMultiplierAndAddsSpins(
        int collectedBefore, int retriggersBefore, int wilds, int retriggersAfter, int spinsAdded)
    {
        var session = Feature(spinsLeft: 5, collectedBefore, retriggersBefore);
        var before = _engine.Snapshot(session);
        var multiplierBefore = session.Multiplier;

        var r = _engine.Apply(session, null, WildGrid(wilds));

        Assert.Equal(collectedBefore + wilds, r.State.WildsCollected);
        Assert.Equal(retriggersAfter, r.State.Retriggers);
        Assert.Equal(Contract.Multipliers[retriggersAfter], r.State.Multiplier);
        Assert.Equal(spinsAdded > 0, r.Retriggered);                     // true once, however many thresholds
        Assert.Equal(5 - 1 + spinsAdded, r.State.FreeSpinsRemaining);
        Assert.Equal(5 + spinsAdded, r.State.FreeSpinsTotal);
        Assert.Equal(7m * wilds * multiplierBefore, r.CollectWin);        // the new multiplier applies from the next spin
        Assert.False(r.FeatureEnded);
        SpinChecks.Transition(before, 1m, r);
    }

    [Fact]
    public void RetriggerOnTheLastFreeSpin_KeepsTheFeatureAlive()
    {
        var session = Feature(spinsLeft: 1, wildsCollected: 3, retriggers: 0);

        var r = _engine.Apply(session, null, WildGrid(wilds: 1));

        Assert.True(r.Retriggered);
        Assert.False(r.FeatureEnded);
        Assert.Equal((10, 11, 2), (r.State.FreeSpinsRemaining, r.State.FreeSpinsTotal, r.State.Multiplier));
    }

    [Fact]
    public void LastFreeSpin_EndsTheFeature_SummaryStaysUntilTheNextPaidSpinClearsIt()
    {
        var session = Feature(spinsLeft: 2, wildsCollected: 0, retriggers: 0, bet: 2m);

        var first = _engine.Apply(session, null, WildGrid(wilds: 1, bet: 2m));   // collect 14 × 1 × 1
        var last = _engine.Apply(session, null, WildGrid(wilds: 2, bet: 2m));    // collect 14 × 2 × 1

        Assert.False(first.FeatureEnded);
        Assert.True(last.FeatureEnded);
        Assert.Equal(new GameState(1000m + 14m + 28m, 0, 2, 1, 3, 0, 42m, 2m), last.State);
        Assert.Equal(last.State, _engine.Snapshot(session));                       // summary kept

        var paid = _engine.Apply(session, 1m, Grids.Blank());

        Assert.False(paid.WasFreeSpin);
        Assert.Equal(new GameState(1000m + 42m - 1m, 0, 0, 1, 0, 0, 0m, 0m), paid.State);
    }
}
