namespace MyApp.Tests.Engine;

public class InfoAndResetTests
{
    private readonly SlotEngine _engine = new();

    [Fact]
    public void Info_MatchesContract()
    {
        var info = _engine.Info();

        Assert.Equal(Contract.Bets, info.Bets);
        Assert.Equal(Contract.Multipliers, info.Multipliers);
        Assert.Equal(Contract.WildsPerRetrigger, info.WildsPerRetrigger);
    }

    [Fact]
    public void Info_HasTenDistinctPaylines_WithRowIndexesZeroToTwo()
    {
        var lines = _engine.Info().Paylines;

        Assert.Equal(Contract.Lines, lines.Length);
        Assert.All(lines, line =>
        {
            Assert.Equal(Contract.Reels, line.Length);
            Assert.All(line, row => Assert.InRange(row, 0, Contract.Rows - 1));
        });
        Assert.Equal(lines.Length, lines.Select(l => string.Join(",", l)).Distinct().Count());
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, lines[0]); // line 0 = middle row, as in the contract example
    }

    [Fact]
    public void Info_PaytableKeysAreEnumNames_IncludingFishAndWild_ButNotScatter()
    {
        var paytable = _engine.Info().Paytable;

        var expected = Enum.GetNames<Symbol>().Where(n => n != nameof(Symbol.Scatter));
        Assert.Equal(expected.Order(), paytable.Keys.Order());
        Assert.All(paytable.Values, pays =>
        {
            Assert.Equal(3, pays.Length);                       // pay for 3, 4, 5
            Assert.True(pays[0] > 0 && pays[0] < pays[1] && pays[1] < pays[2]);
        });
    }

    [Fact]
    public void Info_ScatterAwardsMatchContract()
    {
        var scatter = _engine.Info().Scatter;

        Assert.Equal(new[] { "3", "4", "5" }, scatter.Keys.Order());
        foreach (var (count, (pay, freeSpins)) in Contract.Scatter)
            Assert.Equal(new ScatterAward(pay, freeSpins), scatter[count.ToString()]);
    }

    [Fact]
    public void NewSession_StartsAt1000_WithNoFeature()
    {
        var state = _engine.Snapshot(new GameSession("t"));

        Assert.Equal(new GameState(1000m, 0, 0, 1, 0, 0, 0m, 0m), state);
    }

    [Fact]
    public void Reset_Restores1000_AndClearsFeature()
    {
        var session = new GameSession("t")
        {
            Balance = 12.34m,
            FreeSpinsRemaining = 7,
            FreeSpinsTotal = 20,
            FreeSpinBet = 5m,
            Multiplier = 3,
            WildsCollected = 9,
            Retriggers = 2,
            FeatureWin = 88m,
        };

        var state = _engine.Reset(session);

        // Records compare by value, so one Assert.Equal checks every field.
        Assert.Equal(new GameState(1000m, 0, 0, 1, 0, 0, 0m, 0m), state);
        Assert.Equal(state, _engine.Snapshot(session));
    }
}
