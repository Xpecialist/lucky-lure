namespace MyApp.Tests.Engine;

/// <summary>
/// Deterministic rule tests on hand-built grids, through the internal SlotEngine.Apply seam
/// (same code path as Spin, only the grids are supplied instead of drawn).
/// </summary>
public class FixedGridTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply
    private readonly int[][] _paylines;

    public FixedGridTests() => _paylines = _engine.Info().Paylines;

    private static Cell C(Symbol s) => new(s, null);

    /// <summary>Builds grid[reel][row] from three rows written left to right, as they look on screen.</summary>
    private static Cell[][] Grid(Symbol[] top, Symbol[] middle, Symbol[] bottom) =>
        [.. Enumerable.Range(0, Contract.Reels).Select(r => new[] { C(top[r]), C(middle[r]), C(bottom[r]) })];

    // Rows that never form a run on any payline: alternating symbols.
    private static readonly Symbol[] Top = [Symbol.Jack, Symbol.Queen, Symbol.Jack, Symbol.Queen, Symbol.Jack];
    private static readonly Symbol[] Bottom = [Symbol.King, Symbol.Ace, Symbol.King, Symbol.Ace, Symbol.King];

    [Fact]
    public void FiveTensOnTheMiddleLine_PayLine0Only()
    {
        var session = new GameSession("t");
        var grid = Grid(Top, [Symbol.Ten, Symbol.Ten, Symbol.Ten, Symbol.Ten, Symbol.Ten], Bottom);

        var r = _engine.Apply(session, 1m, grid);

        var win = Assert.Single(r.LineWins);
        Assert.Equal((0, Symbol.Ten, 5), (win.Line, win.Symbol, win.Count));
        Assert.Equal(10m, win.Win);                 // Ten×5 pays 100 line bets; line bet = 1 / 10
        Assert.Equal(10m, r.TotalWin);
        Assert.Equal(1000m - 1m + 10m, r.State.Balance);
        Assert.Empty(r.Respins);
        SpinChecks.LineWins(r, _paylines);
        SpinChecks.Money(r);
    }

    [Fact]
    public void TwoHighScatters_NudgeOnce_AndAThirdScatterOnTheRespinTriggersFreeSpins()
    {
        var session = new GameSession("t");
        var before = _engine.Snapshot(session);
        // Scatters on reel 0 row 0 and reel 3 row 1 → nudge with reels 0 and 3 held.
        var grid = Grid(
            [Symbol.Scatter, Symbol.Queen, Symbol.Jack, Symbol.Queen, Symbol.Jack],
            [Symbol.Rod, Symbol.Float, Symbol.Rod, Symbol.Scatter, Symbol.Rod],
            Bottom);
        // Held reels shifted down one row with a new top symbol; reel 2 respins and lands a third Scatter.
        var respin = Grid(
            [Symbol.Ace, Symbol.Queen, Symbol.Jack, Symbol.King, Symbol.Jack],
            [Symbol.Scatter, Symbol.Float, Symbol.Rod, Symbol.Queen, Symbol.Rod],
            [Symbol.Rod, Symbol.Ace, Symbol.Scatter, Symbol.Scatter, Symbol.King]);

        var r = _engine.Apply(session, 1m, grid, respin);

        var step = Assert.Single(r.Respins);
        Assert.Equal([0, 3], step.HeldReels);
        Assert.Equal(3, r.ScatterCount);           // from the final grid
        Assert.Equal(Contract.Scatter[3].FreeSpins, r.FreeSpinsAwarded);
        Assert.Equal(Contract.Scatter[3].Pay, r.ScatterWin);
        SpinChecks.Grid(r);                         // includes the shift-down rule vs the previous grid
        SpinChecks.Money(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void ScatterInTheBottomRow_DoesNotNudge()
    {
        var grid = Grid(
            [Symbol.Scatter, Symbol.Queen, Symbol.Jack, Symbol.Queen, Symbol.Jack],
            [Symbol.Rod, Symbol.Float, Symbol.Rod, Symbol.Float, Symbol.Rod],
            [Symbol.King, Symbol.Ace, Symbol.King, Symbol.Scatter, Symbol.King]);

        var r = _engine.Apply(new GameSession("t"), 1m, grid); // would throw if a respin grid were requested

        Assert.Empty(r.Respins);
        Assert.Equal(2, r.ScatterCount);
        Assert.Equal(0, r.FreeSpinsAwarded);
    }
}
