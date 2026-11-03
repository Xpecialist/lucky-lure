namespace MyApp.Tests.Engine;

/// <summary>Free-spin feature flow. State is arranged through GameSession's public setters.</summary>
public class FeatureTests
{
    private readonly SlotEngine _engine = new();

    [Fact]
    public void TriggerSpins_AwardFreeSpinsByScatterCount()
    {
        var session = Play.RichSession();
        var seen = new HashSet<int>();

        for (var feature = 0; feature < 40; feature++)
        {
            session.ClearFeature();
            var trigger = Play.Until(_engine, session, 1m, r => r.FreeSpinsAwarded > 0, maxSpins: 20_000);

            Assert.InRange(trigger.ScatterCount, 3, 5);
            Assert.Equal(Contract.Scatter[trigger.ScatterCount].FreeSpins, trigger.FreeSpinsAwarded);
            Assert.Equal(Contract.Scatter[trigger.ScatterCount].Pay * 1m, trigger.ScatterWin);
            Assert.False(trigger.WasFreeSpin);
            Assert.Equal(trigger.FreeSpinsAwarded, trigger.State.FreeSpinsRemaining);
            Assert.Equal(trigger.FreeSpinsAwarded, trigger.State.FreeSpinsTotal);
            Assert.Equal(1m, trigger.State.FreeSpinBet);
            seen.Add(trigger.ScatterCount);
        }

        Assert.Contains(3, seen); // 3 scatters is by far the most common trigger
    }

    [Fact]
    public void FreeSpins_DoNotDeduct_AndUseTheLockedBet()
    {
        var session = Play.InFeature(freeSpins: 200, freeSpinBet: 2m);

        foreach (var (before, r) in Play.Spins(_engine, session, bet: 0.10m, count: 200))
        {
            Assert.True(r.WasFreeSpin);
            Assert.Equal(2m, r.Bet);
            Assert.Equal(before.Balance + r.TotalWin, r.State.Balance);
            Assert.Equal(2m, r.State.FreeSpinBet);
        }
    }

    [Fact]
    public void CollectWin_IsZeroWithoutWilds_AndFishSumTimesWildsTimesMultiplierWithWilds()
    {
        var session = Play.InFeature(freeSpins: 1_000_000);
        int withWilds = 0, withoutWilds = 0;

        foreach (var (before, r) in Play.Spins(_engine, session, 1m, 5_000))
        {
            var fishSum = Play.Cells(r).Where(c => c.Symbol == Symbol.Fish).Sum(c => c.Value!.Value);
            if (r.WildCount == 0)
            {
                Assert.Equal(0m, r.CollectWin);
                withoutWilds++;
            }
            else
            {
                // Multiplier in effect BEFORE this spin (a retrigger on this spin applies from the next one).
                Assert.Equal(Math.Round(fishSum * r.WildCount * before.Multiplier, 2), r.CollectWin);
                withWilds++;
            }
        }

        Assert.True(withWilds > 0 && withoutWilds > 0, $"Sample too narrow: {withWilds} with, {withoutWilds} without wilds.");
    }

    [Fact]
    public void Multiplier_Steps1To2To3To10_AndSpinsGrowBy10PerRetrigger()
    {
        var session = Play.InFeature(freeSpins: 1_000_000);
        var multipliersSeen = new List<int> { session.Multiplier };

        for (var i = 0; i < 50_000 && session.Retriggers < 3; i++)
        {
            var before = _engine.Snapshot(session);
            var r = _engine.Spin(session, null);
            var added = r.State.Retriggers - before.Retriggers;

            Assert.Equal(added > 0, r.Retriggered);
            Assert.Equal(before.FreeSpinsTotal + 10 * added, r.State.FreeSpinsTotal);
            Assert.Equal(before.FreeSpinsRemaining - 1 + 10 * added, r.State.FreeSpinsRemaining);
            for (var k = 0; k < added; k++)
                multipliersSeen.Add(Contract.Multipliers[before.Retriggers + k + 1]);
            Assert.Equal(multipliersSeen[^1], r.State.Multiplier);
        }

        Assert.Equal(Contract.Multipliers, multipliersSeen);
    }

    [Fact]
    public void Retriggers_AreCappedAtThree()
    {
        var session = Play.InFeature(freeSpins: 1_000_000);
        session.Retriggers = 3;
        session.Multiplier = 10;
        session.WildsCollected = 12;

        foreach (var (_, r) in Play.Spins(_engine, session, 1m, 3_000))
        {
            Assert.False(r.Retriggered);
            Assert.Equal(3, r.State.Retriggers);
            Assert.Equal(10, r.State.Multiplier);
        }
        Assert.True(session.WildsCollected > 12, "No wilds landed in the sample.");
    }

    [Fact]
    public void FeatureEnded_IsTrueExactlyOnTheSpinThatReachesZero_AndSummaryIsKept()
    {
        var session = Play.RichSession();
        var trigger = Play.Until(_engine, session, 1m, r => r.FreeSpinsAwarded > 0, maxSpins: 20_000);

        var freeSpinsPlayed = 0;
        var featureWins = 0m;
        SpinResult last;
        do
        {
            last = _engine.Spin(session, 1m);
            freeSpinsPlayed++;
            featureWins += last.TotalWin;
            Assert.True(last.WasFreeSpin);
            Assert.Equal(last.State.FreeSpinsRemaining == 0, last.FeatureEnded);
        } while (!last.FeatureEnded && freeSpinsPlayed < 1_000);

        Assert.True(last.FeatureEnded);
        Assert.Equal(last.State.FreeSpinsTotal, freeSpinsPlayed);
        Assert.Equal(trigger.FreeSpinsAwarded + 10 * last.State.Retriggers, last.State.FreeSpinsTotal);
        Assert.Equal(featureWins, last.State.FreeSpinWin); // excludes the trigger's scatter win

        // Summary stays visible until the next paid spin...
        Assert.Equal(last.State, _engine.Snapshot(session));
        Assert.Equal(0, last.State.FreeSpinsRemaining);

        // ...which clears it (unless that very spin triggers a new feature).
        var next = _engine.Spin(session, 1m);
        Assert.False(next.WasFreeSpin);
        if (next.FreeSpinsAwarded == 0)
            Assert.Equal(new GameState(next.State.Balance, 0, 0, 1, 0, 0, 0m, 0m), next.State);
    }

    [Fact]
    public void LastFreeSpin_WithoutRetrigger_EndsFeature()
    {
        var session = Play.InFeature(freeSpins: 1);

        var r = _engine.Spin(session, null);

        // From 0 wilds collected, one spin would need 4+ wilds to retrigger: practically never.
        if (!r.Retriggered)
        {
            Assert.True(r.FeatureEnded);
            Assert.Equal(0, r.State.FreeSpinsRemaining);
        }
    }
}
