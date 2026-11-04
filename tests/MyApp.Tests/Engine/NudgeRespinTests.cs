namespace MyApp.Tests.Engine;

/// <summary>
/// Nudge respin (API_CONTRACT.md "Nudge respin"), exactly. Two ways in:
/// - Apply(grid, respinGrids): the test supplies every grid; checks when respins happen and how wins add up.
/// - Spin with a <see cref="ScriptedRandom"/>: the engine builds the respin grids itself; checks the shift.
/// See also FixedGridTests (third Scatter on a respin triggers free spins, bottom-row Scatter doesn't nudge).
/// </summary>
public class NudgeRespinTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource());
    private readonly int[][] _paylines;

    public NudgeRespinTests() => _paylines = _engine.Info().Paylines;

    private static readonly Cell Scatter = new(Symbol.Scatter, null);

    /// <summary>The respin a legal nudge could produce: Scatter reels shift down with a TackleBox on top, the rest blank.</summary>
    private static Cell[][] Nudged(Cell[][] grid)
    {
        var blank = Grids.Blank();
        return
        [
            .. Enumerable.Range(0, Contract.Reels).Select(reel => grid[reel].Any(c => c.Symbol == Symbol.Scatter)
                ? new[] { new Cell(Symbol.TackleBox, null), grid[reel][0], grid[reel][1] }
                : blank[reel]),
        ];
    }

    [Theory]
    [InlineData("0,0 3,1", 1)]       // top + middle: one respin (the middle one reaches the bottom)
    [InlineData("1,1 4,1", 1)]       // both middle
    [InlineData("0,1 4,0", 1)]
    [InlineData("0,0 2,0", 2)]       // both top: they qualify again after one shift → 2 respins
    [InlineData("0,0 2,2", 0)]       // one in the bottom row
    [InlineData("2,0", 0)]           // a single Scatter
    [InlineData("0,0 2,0 4,0", 0)]   // three Scatters trigger directly, no nudge
    public void RespinCount_FollowsTheCondition(string scatterSpots, int expectedRespins)
    {
        var session = new GameSession("t");
        var before = _engine.Snapshot(session);
        var grid = Grids.With(Grids.Blank(), Scatter, Grids.Positions(scatterSpots));
        var respin1 = Nudged(grid);
        var respin2 = Nudged(respin1);

        // Apply uses only as many respin grids as the rules ask for.
        var r = _engine.Apply(session, 1m, grid, respin1, respin2);

        Assert.Equal(expectedRespins, r.Respins.Count);
        Assert.Equal(new[] { respin1, respin2 }.Take(expectedRespins), r.Respins.Select(s => s.Grid));
        var scatterReels = Grids.Positions(scatterSpots).Select(p => p.Reel).ToArray();
        Assert.All(r.Respins, step => Assert.Equal(scatterReels, step.HeldReels));
        SpinChecks.Grid(r);
        SpinChecks.Transition(before, 1m, r);
    }

    [Fact]
    public void AtMostTwoRespins_EvenIfTheGridStillQualifies()
    {
        // Deliberately impossible respin grids (the Scatters don't move) to show the cap is enforced in code,
        // not only by the geometry of the shift.
        var grid = Grids.With(Grids.Blank(), Scatter, (0, 0), (2, 0));

        var r = _engine.Apply(new GameSession("t"), 1m, grid, grid, grid, grid);

        Assert.Equal(2, r.Respins.Count);
    }

    [Fact]
    public void NoNudgeInFreeSpins()
    {
        // Scatters can't land in free spins; through the seam we can still show the rule is never checked there.
        var grid = Grids.With(Grids.Blank(), Scatter, (0, 0), (2, 0));

        var r = _engine.Apply(Play.InFeature(freeSpins: 5), null, grid); // would throw if a respin were requested

        Assert.Empty(r.Respins);
        Assert.Equal((0, 0m), (r.FreeSpinsAwarded, r.ScatterWin));  // and scatters never pay in free spins
    }

    [Fact]
    public void LineWinsPayOnEveryGrid_ScatterPayComesFromTheFinalGrid()
    {
        const decimal bet = 0.20m; // line bet 0.02
        var session = new GameSession("t");
        // Scatters at reel 0 top and reel 3 middle; line 0 pays Ace×3.
        var grid = Grids.Parse(bet,
            "S Q J Q J",
            "A A A S R",
            "K A K A K");
        // Held reels 0 and 3 shifted down (reel 3's Scatter reaches the bottom → no more respins);
        // reel 4 lands a third Scatter; line 2 pays Ace×3.
        var respin = Grids.Parse(bet,
            "TB Q J TB S",
            "S FL R Q R",
            "A A A S K");

        var r = _engine.Apply(session, bet, grid, respin);

        var lineWin = Assert.Single(r.LineWins);
        Assert.Equal((0, Symbol.Ace, 3, 0.40m), (lineWin.Line, lineWin.Symbol, lineWin.Count, lineWin.Win));
        var step = Assert.Single(r.Respins);
        Assert.Equal([0, 3], step.HeldReels);
        var respinWin = Assert.Single(step.LineWins);
        Assert.Equal((2, Symbol.Ace, 3, 0.40m), (respinWin.Line, respinWin.Symbol, respinWin.Count, respinWin.Win));

        Assert.Equal(3, r.ScatterCount);                              // final grid (the first one had 2)
        Assert.Equal(Contract.Scatter[3].Pay * bet, r.ScatterWin);     // 0.40
        Assert.Equal(Contract.Scatter[3].FreeSpins, r.FreeSpinsAwarded);
        Assert.Equal(0.40m + 0.40m + 0.40m, r.TotalWin);
        Assert.Equal(1000m - bet + 1.20m, r.State.Balance);
        SpinChecks.Grid(r);
        SpinChecks.Money(r);
        SpinChecks.LineWins(r, _paylines);
    }

    // ---- The engine's own respin grids (production Nudge), driven by a scripted RNG ----

    /// <summary>RNG answers for a plain base-game reel (no Scatter) with these symbols top to bottom.</summary>
    private static IEnumerable<int> Reel(params Symbol[] cells) =>
        [SymbolRolls.NoScatter, .. cells.Select(s => SymbolRolls.Base[s])];

    /// <summary>A base-game reel with a Scatter in <paramref name="row"/> and these symbols in the other rows.</summary>
    private static IEnumerable<int> ScatterReel(int row, params Symbol[] others) =>
        [SymbolRolls.Scatter, row, .. others.Select(s => SymbolRolls.Base[s])];

    /// <summary>A held reel draws exactly one number: the new top symbol (no scatter roll).</summary>
    private static IEnumerable<int> NewTop(Symbol s) => [SymbolRolls.Base[s]];

    /// <summary>A Fish draws its value right after its symbol; any answer works, the test reads the value back.</summary>
    private const int FishValueRoll = 0;

    [Fact]
    public void HeldReelsShiftDownExactly_WithANewNonScatterOnTop_TwiceFromTheTopRow()
    {
        Symbol[] reel1 = [Symbol.Queen, Symbol.Float, Symbol.Ace];
        Symbol[] reel3 = [Symbol.Queen, Symbol.Float, Symbol.Ace];
        Symbol[] reel4 = [Symbol.Jack, Symbol.Rod, Symbol.King];
        var rng = new ScriptedRandom([
            // Landed grid: Scatters on top of reels 0 and 2; reel 0 has a Fish in the middle.
            .. ScatterReel(0, Symbol.Fish), FishValueRoll, SymbolRolls.Base[Symbol.King],
            .. Reel(reel1), .. ScatterReel(0, Symbol.Rod, Symbol.King), .. Reel(reel3), .. Reel(reel4),
            // Respin 1: reels 0 and 2 held (one draw each), the others respin.
            .. NewTop(Symbol.TackleBox), .. Reel(reel1), .. NewTop(Symbol.TackleBox), .. Reel(reel3), .. Reel(reel4),
            // Respin 2: the Scatters are in the middle row now → nudge again.
            .. NewTop(Symbol.Dragonfly), .. Reel(reel1), .. NewTop(Symbol.Dragonfly), .. Reel(reel3), .. Reel(reel4),
        ]);
        var engine = new SlotEngine(rng);
        var session = new GameSession("t");
        var before = engine.Snapshot(session);

        var r = engine.Spin(session, 1m);

        Assert.Equal(0, rng.Remaining); // every scripted number used, none missing
        var fish = r.Grid[0][1];
        Assert.Equal(Symbol.Fish, fish.Symbol);
        Assert.Equal(2, r.Respins.Count);
        Assert.All(r.Respins, step => Assert.Equal([0, 2], step.HeldReels));

        var (g0, g1, g2) = (r.Grid, r.Respins[0].Grid, r.Respins[1].Grid);
        Cell tackleBox = new(Symbol.TackleBox, null), dragonfly = new(Symbol.Dragonfly, null);
        Assert.Equal([tackleBox, Scatter, fish], g1[0]);          // the Fish keeps its value
        Assert.Equal([tackleBox, Scatter, g0[2][1]], g1[2]);
        Assert.Equal([dragonfly, tackleBox, Scatter], g2[0]);
        Assert.Equal([dragonfly, tackleBox, Scatter], g2[2]);
        Assert.Equal(2, r.ScatterCount);                          // both in the bottom row: no third Scatter
        Assert.Equal(0, r.FreeSpinsAwarded);
        SpinChecks.Grid(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _paylines);
    }
}
