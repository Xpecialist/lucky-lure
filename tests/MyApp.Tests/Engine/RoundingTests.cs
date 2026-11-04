namespace MyApp.Tests.Engine;

/// <summary>
/// Money rounding. The engine rounds every amount to 2 decimals with MidpointRounding.AwayFromZero
/// (SlotEngine.Round). With contract values (integer pays and fish multipliers × bets of 0.10 and up) no amount ever
/// has a third decimal, so the rounding mode can only be observed with off-contract Fish values via the Apply seam.
/// </summary>
public class RoundingTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply

    public static TheoryData<decimal, int, decimal> SmallBetCollects() => new()
    {
        // bet, Wilds, expected collect: Fish 2 + 5 = 7 × bet, multiplier ×10 (retriggers maxed).
        { 0.10m, 1, 7.00m },
        { 0.10m, 3, 21.00m },
        { 0.20m, 1, 14.00m },
        { 0.20m, 3, 42.00m },
    };

    [Theory]
    [MemberData(nameof(SmallBetCollects))]
    public void CollectAtSmallBets_IsExactInCents(decimal bet, int wilds, decimal expected)
    {
        var session = Play.InFeature(freeSpins: 5, freeSpinBet: bet);
        (session.Retriggers, session.Multiplier, session.WildsCollected) = (3, 10, 12);
        session.Balance = 1000.10m;
        var grid = Grids.With(Grids.Parse(bet, "F2 Q J Q J", "F5 FL R FL R", Grids.BlankBottom),
            new Cell(Symbol.Wild, null), [.. new[] { (2, 0), (3, 1), (4, 2) }.Take(wilds)]);

        var r = _engine.Apply(session, null, grid);

        Assert.Equal(expected, r.CollectWin);
        Assert.Equal(1000.10m + expected, r.State.Balance);
        SpinChecks.Money(r);
    }

    [Theory]
    [InlineData(0.10, Symbol.Ten, 3, 0.07)]   // 7 line bets × 0.01
    [InlineData(0.10, Symbol.Queen, 4, 0.40)]
    [InlineData(0.20, Symbol.Ten, 3, 0.14)]
    [InlineData(0.20, Symbol.Float, 5, 30.00)]
    public void LineWinsAtSmallBets_AreExactInCents(double betValue, Symbol symbol, int count, double expectedValue)
    {
        // decimal can't be an attribute argument, so the rows use double and convert (exact for these values).
        var (bet, expected) = ((decimal)betValue, (decimal)expectedValue);
        var middle = string.Join(' ', Enumerable.Range(0, 5).Select(i => i < count ? Code(symbol) : i % 2 == 0 ? "R" : "FL"));

        var r = _engine.Apply(new GameSession("t"), bet, Grids.Parse(bet, Grids.BlankTop, middle, Grids.BlankBottom));

        var win = Assert.Single(r.LineWins);
        Assert.Equal(expected, win.Win);
        Assert.Equal(1000m - bet + expected, r.State.Balance);
        SpinChecks.Money(r);
    }

    private static string Code(Symbol s) => s switch
    {
        Symbol.Ten => "10",
        Symbol.Queen => "Q",
        Symbol.Float => "FL",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    [Theory]
    // Off-contract Fish value (only possible through the seam), Wilds → collect after rounding.
    [InlineData("0.005", 1, "0.01")]    // midpoint away from zero (banker's rounding would give 0.00)
    [InlineData("0.125", 1, "0.13")]    // banker's: 0.12
    [InlineData("0.135", 1, "0.14")]    // banker's: 0.14 too
    [InlineData("0.004", 1, "0.00")]
    [InlineData("0.0025", 2, "0.01")]   // the product is rounded once (0.005), not each factor
    public void Collect_RoundsTheProductToCents_MidpointAwayFromZero(string fishValue, int wilds, string expected)
    {
        var session = Play.InFeature(freeSpins: 5);
        var grid = Grids.With(Grids.Blank(), new Cell(Symbol.Fish, decimal.Parse(fishValue, System.Globalization.CultureInfo.InvariantCulture)), (0, 1));
        grid = Grids.With(grid, new Cell(Symbol.Wild, null), [.. new[] { (2, 0), (3, 1) }.Take(wilds)]);

        var r = _engine.Apply(session, null, grid);

        var cents = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(cents, r.CollectWin);
        Assert.Equal(cents, r.TotalWin);
        Assert.Equal(1000m + cents, r.State.Balance);
        Assert.True(Play.HasAtMostTwoDecimals(r.State.Balance));
    }

    [Fact]
    public void Snapshot_ShowsTheBalanceWithAtMostTwoDecimals()
    {
        var session = new GameSession("t") { Balance = 1000.005m };

        Assert.Equal(1000.01m, _engine.Snapshot(session).Balance);
    }
}
