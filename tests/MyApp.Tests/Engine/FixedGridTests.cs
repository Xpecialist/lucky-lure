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

    // ---- Net cast (free spins) ----

    // A free-spin grid with one Wild (reel 0, middle) and no Fish, and no line wins.
    private static readonly Symbol[] NetMiddle = [Symbol.Wild, Symbol.Float, Symbol.Rod, Symbol.Float, Symbol.Rod];

    /// <summary>A session mid-feature at multiplier ×2 (one retrigger already reached).</summary>
    private static GameSession AtMultiplier2(decimal bet)
    {
        var s = Play.InFeature(freeSpins: 5, freeSpinBet: bet);
        (s.Retriggers, s.Multiplier, s.WildsCollected) = (1, 2, 4);
        return s;
    }

    private void CheckAll(GameState before, SpinResult r, decimal bet)
    {
        SpinChecks.Grid(r);
        SpinChecks.Money(r);
        SpinChecks.Transition(before, bet, r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void SmallNet_TurnsThreeCellsIntoFish_AndCollectsOnTheTransformedGrid()
    {
        var session = AtMultiplier2(bet: 1m);
        var before = _engine.Snapshot(session);
        var landed = Grid(Top, NetMiddle, Bottom);
        var plan = new NetPlan(NetSize.Small, [[1, 0], [3, 2], [4, 1]], [5, 10, 2]);

        var r = _engine.Apply(session, null, landed, plan);

        var net = Assert.IsType<NetCast>(r.Net);
        Assert.Equal(NetSize.Small, net.Size);
        Assert.Same(landed, r.Grid);                                    // top-level grid = the landed grid
        Assert.Equal(new Cell(Symbol.Fish, 5m), net.Grid[1][0]);
        Assert.Equal(new Cell(Symbol.Fish, 10m), net.Grid[3][2]);
        Assert.Equal(new Cell(Symbol.Fish, 2m), net.Grid[4][1]);
        Assert.Equal(1, r.WildCount);
        Assert.Equal((5m + 10m + 2m) * 1 * 2, r.CollectWin);            // fishSum × wilds × multiplier
        Assert.Empty(r.LineWins);
        Assert.Equal(r.CollectWin, r.TotalWin);
        CheckAll(before, r, 1m);
    }

    [Fact]
    public void BigNet_TurnsFiveCellsIntoFish_ValuesScaleWithTheBet()
    {
        var session = AtMultiplier2(bet: 2m);
        var before = _engine.Snapshot(session);
        // Two Wilds: reel 0 middle and reel 4 bottom.
        var landed = Grid(Top, NetMiddle, [Symbol.King, Symbol.Ace, Symbol.King, Symbol.Ace, Symbol.Wild]);
        var plan = new NetPlan(NetSize.Big, [[0, 2], [1, 0], [2, 2], [3, 1], [4, 0]], [100, 2, 5, 2, 15]);

        var r = _engine.Apply(session, null, landed, plan);

        var net = Assert.IsType<NetCast>(r.Net);
        Assert.Equal(NetSize.Big, net.Size);
        Assert.Equal(5, net.Positions.Length);
        Assert.Equal(new Cell(Symbol.Fish, 200m), net.Grid[0][2]);      // 100 × bet 2
        Assert.Equal(2, r.WildCount);
        Assert.Equal((100 + 2 + 5 + 2 + 15) * 2m * 2 * 2, r.CollectWin); // fishSum (× bet) × 2 wilds × multiplier 2
        Assert.Empty(r.LineWins);
        Assert.Equal(6, r.State.WildsCollected);                        // the net adds no Wilds to the meter
        CheckAll(before, r, 2m);
    }

    [Fact]
    public void QualifyingGrid_WithNoNetCast_CollectsNothing()
    {
        var session = AtMultiplier2(bet: 1m);
        var r = _engine.Apply(session, null, Grid(Top, NetMiddle, Bottom), net: null);

        Assert.Null(r.Net);
        Assert.Equal(0m, r.CollectWin);
        Assert.Equal(0m, r.TotalWin);
    }

    [Fact]
    public void NetCast_NeverHappensInTheBaseGame()
    {
        var plan = new NetPlan(NetSize.Small, [[1, 0], [3, 2], [4, 1]], [5, 10, 2]);

        var r = _engine.Apply(new GameSession("t"), 1m, Grid(Top, [Symbol.Rod, Symbol.Float, Symbol.Rod, Symbol.Float, Symbol.Rod], Bottom), plan);

        Assert.Null(r.Net);
    }

    // ---- Scatter is not a wild on paylines (line 0 = middle row) ----

    [Fact]
    public void ScatterInTheMiddleOfAceRun_BreaksIt()
    {
        var r = _engine.Apply(new GameSession("t"), 1m,
            Grid(Top, [Symbol.Ace, Symbol.Ace, Symbol.Scatter, Symbol.Ace, Symbol.Ace], Bottom));

        Assert.DoesNotContain(r.LineWins, w => w.Line == 0);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void ScatterOnReel1_StartsNoRun()
    {
        var r = _engine.Apply(new GameSession("t"), 1m,
            Grid(Top, [Symbol.Scatter, Symbol.Ace, Symbol.Ace, Symbol.Ace, Symbol.Jack], Bottom));

        Assert.DoesNotContain(r.LineWins, w => w.Line == 0);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void ScatterAfterThreeAces_StopsTheRunAtThree()
    {
        var r = _engine.Apply(new GameSession("t"), 1m,
            Grid(Top, [Symbol.Ace, Symbol.Ace, Symbol.Ace, Symbol.Scatter, Symbol.Ace], Bottom));

        var win = Assert.Single(r.LineWins, w => w.Line == 0);
        Assert.Equal((Symbol.Ace, 3), (win.Symbol, win.Count));
        Assert.Equal([[0, 1], [1, 1], [2, 1]], win.Positions);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void WildNextToAScatter_DoesNotSubstituteForIt()
    {
        // Free spin (Wilds only land there); the grid qualifies for a net cast, forced to none.
        var session = Play.InFeature(freeSpins: 5);
        var r = _engine.Apply(session, null,
            Grid(Top, [Symbol.Wild, Symbol.Scatter, Symbol.Ace, Symbol.Ace, Symbol.Ace], Bottom), net: null);

        Assert.DoesNotContain(r.LineWins, w => w.Line == 0);
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
