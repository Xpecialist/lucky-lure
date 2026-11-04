namespace MyApp.Tests.Engine;

/// <summary>Scatter pays and the free-spin trigger, exactly (the random-spin version is in FeatureTests).</summary>
public class ScatterPayTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply

    // Scatters "anywhere": different reels and rows, including the bottom row. 3+ Scatters never nudge.
    private static readonly (int Reel, int Row)[] ScatterSpots = [(0, 2), (2, 0), (4, 1), (1, 2), (3, 0)];

    public static TheoryData<int, decimal> Cases() => new()
    {
        { 3, 0.10m }, { 4, 0.10m }, { 5, 0.10m },
        { 3, 2m }, { 4, 2m }, { 5, 2m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScattersAnywhere_PayTimesTotalBet_AndAwardFreeSpins(int scatters, decimal bet)
    {
        var session = new GameSession("t");
        var before = _engine.Snapshot(session);
        var grid = Grids.With(Grids.Blank(bet), new Cell(Symbol.Scatter, null), ScatterSpots[..scatters]);
        var (pay, freeSpins) = Contract.Scatter[scatters];

        var r = _engine.Apply(session, bet, grid); // would throw if a nudge respin were requested

        Assert.Empty(r.Respins);
        Assert.Empty(r.LineWins);
        Assert.Equal(scatters, r.ScatterCount);
        Assert.Equal(pay * bet, r.ScatterWin);
        Assert.Equal(pay * bet, r.TotalWin);
        Assert.Equal(freeSpins, r.FreeSpinsAwarded);
        Assert.Equal(new GameState(1000m - bet + pay * bet, freeSpins, freeSpins, 1, 0, 0, 0m, bet), r.State);
        SpinChecks.Transition(before, bet, r);
        SpinChecks.Money(r);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)] // two Scatters with one in the bottom row: no nudge, no pay
    public void FewerThanThreeScatters_PayNothing(int scatters)
    {
        var grid = Grids.With(Grids.Blank(), new Cell(Symbol.Scatter, null), ScatterSpots[..scatters]);

        var r = _engine.Apply(new GameSession("t"), 1m, grid);

        Assert.Equal((scatters, 0m, 0, 0m), (r.ScatterCount, r.ScatterWin, r.FreeSpinsAwarded, r.TotalWin));
    }

    [Fact]
    public void FreeSpinsAfterTheTrigger_UseTheTriggerBet_AndDeductNothing()
    {
        var session = new GameSession("t");
        var trigger = _engine.Apply(session, 2m,
            Grids.With(Grids.Blank(2m), new Cell(Symbol.Scatter, null), ScatterSpots[..3]));
        var afterTrigger = trigger.State.Balance; // 1000 - 2 + 2 × 2 = 1002

        // The player asks for bet 10 during the feature: ignored.
        var free = _engine.Apply(session, 10m, Grids.Blank(2m));

        Assert.True(free.WasFreeSpin);
        Assert.Equal(2m, free.Bet);
        Assert.Equal(1002m, afterTrigger);
        Assert.Equal(afterTrigger, free.State.Balance); // no win, nothing deducted
        Assert.Equal((9, 10, 2m), (free.State.FreeSpinsRemaining, free.State.FreeSpinsTotal, free.State.FreeSpinBet));
    }

    [Fact]
    public void FreeSpin_IsAllowedWithZeroBalance()
    {
        var session = Play.InFeature(freeSpins: 3, freeSpinBet: 5m);
        session.Balance = 0m;

        Assert.Null(_engine.Validate(session, bet: 10m));
        var r = _engine.Apply(session, 10m, Grids.Blank(5m));

        Assert.Equal((true, 5m, 0m), (r.WasFreeSpin, r.Bet, r.State.Balance));
    }
}
