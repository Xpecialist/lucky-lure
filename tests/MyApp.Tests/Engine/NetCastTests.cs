namespace MyApp.Tests.Engine;

/// <summary>
/// Net cast (API_CONTRACT.md "Net cast"), exactly. Small/Big transforms, collect on the transformed grid and
/// "never in the base game" are in FixedGridTests; this class adds the qualifying condition, line wins on the
/// net grid, and the production net roll driven by a scripted RNG.
/// </summary>
public class NetCastTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply
    private readonly int[][] _paylines;

    public NetCastTests() => _paylines = _engine.Info().Paylines;

    private static readonly NetPlan SmallPlan = new(NetSize.Small, [[1, 0], [3, 2], [4, 1]], [2, 5, 10]);

    [Theory]
    [InlineData("R FL W FL R", true)]     // 1 Wild, 0 Fish: qualifies
    [InlineData("R FL W W R", true)]      // 2 Wilds
    [InlineData("F5 FL W FL R", false)]   // a Wild but also a Fish: nothing to rescue
    [InlineData("R FL R FL R", false)]    // no Wild
    [InlineData("F5 FL R FL R", false)]   // Fish, no Wild
    public void NetIsCastOnlyWhenTheLandedGridHasAWildAndNoFish(string middle, bool cast)
    {
        var session = Play.InFeature(freeSpins: 5);
        var before = _engine.Snapshot(session);
        var landed = Grids.Parse(1m, Grids.BlankTop, middle, Grids.BlankBottom);

        var r = _engine.Apply(session, null, landed, SmallPlan); // the plan is used only if the grid qualifies

        Assert.Equal(cast, r.Net is not null);
        var wilds = middle.Split(' ').Count(c => c == "W");
        var fishSum = cast ? 2m + 5m + 10m : middle.Contains("F5") ? 5m : 0m;
        Assert.Equal(fishSum * wilds, r.CollectWin);
        SpinChecks.Grid(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void NetThatBreaksALine_LineIsScoredOnTheNetGrid()
    {
        // Line 0: W-A-A-A-R = Ace×4 without a net. The net turns reel 2's Ace into a Fish: W-F → no win.
        var landed = Grids.Parse(1m, Grids.BlankTop, "W A A A R", Grids.BlankBottom);
        var plan = new NetPlan(NetSize.Small, [[1, 1], [3, 0], [4, 2]], [2, 2, 2]);

        var withoutNet = _engine.Apply(Play.InFeature(freeSpins: 5), null, landed, net: null);
        var session = Play.InFeature(freeSpins: 5);
        var before = _engine.Snapshot(session);
        var withNet = _engine.Apply(session, null, landed, plan);

        var aceWin = Assert.Single(withoutNet.LineWins);
        Assert.Equal((0, Symbol.Ace, 4, 6m), (aceWin.Line, aceWin.Symbol, aceWin.Count, aceWin.Win));
        Assert.NotNull(withNet.Net);
        Assert.Empty(withNet.LineWins);
        Assert.Equal(6m * 1 * 1, withNet.CollectWin);                    // 3 Fish × 2, 1 Wild, ×1
        Assert.Equal(withNet.CollectWin, withNet.TotalWin);
        SpinChecks.Grid(withNet);
        SpinChecks.Transition(before, 1m, withNet);
        SpinChecks.LineWins(withNet, _paylines);
    }

    [Fact]
    public void NetThatCreatesALine_FishRunPaysOnTheNetGrid()
    {
        // The net turns the first three top cells into Fish: line 1 = Fish×3 (40 line bets).
        var landed = Grids.Parse(1m, Grids.BlankTop, "R FL R FL W", Grids.BlankBottom);
        var plan = new NetPlan(NetSize.Small, [[0, 0], [1, 0], [2, 0]], [2, 5, 10]);
        var session = Play.InFeature(freeSpins: 5);
        var before = _engine.Snapshot(session);

        var r = _engine.Apply(session, null, landed, plan);

        var win = Assert.Single(r.LineWins);
        Assert.Equal((1, Symbol.Fish, 3, 4m), (win.Line, win.Symbol, win.Count, win.Win));
        Assert.Equal(17m, r.CollectWin);                                 // (2 + 5 + 10) × 1 Wild × 1
        Assert.Equal(21m, r.TotalWin);
        Assert.Equal(Symbol.Jack, r.Grid[0][0].Symbol);                  // top-level grid stays the landed one
        SpinChecks.Grid(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Theory]
    [InlineData(NetSize.Small, 5)]
    [InlineData(NetSize.Big, 3)]
    public void PlanWithTheWrongNumberOfCells_IsRejected(NetSize size, int cells)
    {
        int[][] topRow = [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0]];
        int[][] positions = [.. topRow.Take(cells)];
        var plan = new NetPlan(size, positions, [.. Enumerable.Repeat(2, cells)]);
        var landed = Grids.Parse(1m, Grids.BlankTop, "R FL W FL R", Grids.BlankBottom);

        Assert.Throws<ArgumentException>(() => _engine.Apply(Play.InFeature(freeSpins: 5), null, landed, plan));
    }

    [Fact]
    public void PlanOnAWildCell_IsRejected()
    {
        var landed = Grids.Parse(1m, Grids.BlankTop, "R FL W FL R", Grids.BlankBottom);
        var plan = new NetPlan(NetSize.Small, [[0, 0], [2, 1], [4, 0]], [2, 2, 2]); // [2,1] is the Wild

        Assert.Throws<ArgumentException>(() => _engine.Apply(Play.InFeature(freeSpins: 5), null, landed, plan));
    }

    // ---- The production net roll (RandomNet), driven by a scripted RNG ----

    /// <summary>Free spins draw one number per cell (no scatter roll), reel by reel, top to bottom.</summary>
    private static IEnumerable<int> FreeGrid(params (int Reel, int Row)[] nonWild) =>
        Enumerable.Range(0, Contract.Reels * Contract.Rows)
            .Select(i => nonWild.Contains((i / Contract.Rows, i % Contract.Rows))
                ? SymbolRolls.Free[Symbol.Ten]
                : SymbolRolls.Free[Symbol.Wild]);

    private const int Cast = 0;       // net chance roll below 450 / 1000
    private const int BigSize = 0;    // size roll below 200 / 1000
    private const int SmallSize = 999;

    [Fact]
    public void BigNet_FromTheRng_TakesExactlyTheFiveNonWildCells()
    {
        (int, int)[] nonWild = [(0, 2), (1, 0), (2, 1), (3, 2), (4, 0)];
        var rng = new ScriptedRandom([
            .. FreeGrid(nonWild), Cast, BigSize,
            0, 0, 0, 0, 0,      // partial Fisher-Yates picks
            0, 0, 0, 0, 0,      // fish values: the first (lowest) value of the net weights
        ]);
        var engine = new SlotEngine(rng);
        var session = Play.InFeature(freeSpins: 5);
        var before = engine.Snapshot(session);

        var r = engine.Spin(session, null);

        Assert.Equal(0, rng.Remaining);
        var net = Assert.IsType<NetCast>(r.Net);
        Assert.Equal(NetSize.Big, net.Size);
        Assert.Equal(nonWild.Select(p => new[] { p.Item1, p.Item2 }), net.Positions); // never a Wild cell
        Assert.Equal(10, r.WildCount);
        var fishValue = net.Grid[0][2].Value!.Value;
        Assert.Equal(5 * fishValue * 10, r.CollectWin);                 // 5 Fish × 10 Wilds × ×1
        SpinChecks.Grid(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _paylines);
    }

    [Fact]
    public void BigNet_WithOnlyFourNonWildCells_IsNotCast()
    {
        var rng = new ScriptedRandom([.. FreeGrid((0, 0), (1, 1), (2, 2), (3, 0)), Cast, BigSize]);
        var engine = new SlotEngine(rng);

        var r = engine.Spin(Play.InFeature(freeSpins: 5), null);

        Assert.Equal(0, rng.Remaining);
        Assert.Null(r.Net);
        Assert.Equal(0m, r.CollectWin);
    }

    [Fact]
    public void SmallNet_FromTheRng_PicksThreeOfTheNonWildCells()
    {
        (int, int)[] nonWild = [(0, 0), (1, 1), (2, 2), (3, 0)];
        // Fisher-Yates answers 0, 0, 0 keep the candidates in reel/row order: the first three are taken.
        var rng = new ScriptedRandom([.. FreeGrid(nonWild), Cast, SmallSize, 0, 0, 0, 0, 0, 0]);
        var engine = new SlotEngine(rng);

        var r = engine.Spin(Play.InFeature(freeSpins: 5), null);

        Assert.Equal(0, rng.Remaining);
        var net = Assert.IsType<NetCast>(r.Net);
        Assert.Equal(NetSize.Small, net.Size);
        Assert.Equal(nonWild.Take(3).Select(p => new[] { p.Item1, p.Item2 }), net.Positions);
        Assert.Equal(Symbol.Ten, net.Grid[3][0].Symbol);                // the 4th non-Wild cell is untouched
        SpinChecks.Grid(r);
    }

    [Fact]
    public void NoNetCast_WhenTheChanceRollMisses()
    {
        var rng = new ScriptedRandom([.. FreeGrid((0, 0), (1, 1), (2, 2)), 999]);
        var engine = new SlotEngine(rng);

        var r = engine.Spin(Play.InFeature(freeSpins: 5), null);

        Assert.Equal(0, rng.Remaining);
        Assert.Null(r.Net);
    }
}
