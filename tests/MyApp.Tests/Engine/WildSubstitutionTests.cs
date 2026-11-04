namespace MyApp.Tests.Engine;

/// <summary>
/// Line evaluation edge cases on hand-built grids: Wild substitution, Fish runs, several lines on one grid.
/// Scatter-breaks-a-run cases live in <see cref="FixedGridTests"/> (Scatter* tests) and are not repeated here.
/// </summary>
public class WildSubstitutionTests
{
    private readonly SlotEngine _engine = new(new CryptoRandomSource()); // RNG unused by Apply
    private readonly GameInfo _info;

    public WildSubstitutionTests() => _info = _engine.Info();

    private decimal LineBets(Symbol symbol, int count) => _info.Paytable[symbol.ToString()][count - 3];

    // Fish (worth 2) on reel 1 top and bottom: every line that starts in row 0 or 2 starts with a Fish, and the
    // next cell is never a Fish, so only lines 0, 7 and 8 (starting in the middle) can pay; 7 and 8 never reach 3.
    private const string FishTop = "F2 J Q J Q";
    private const string FishBottom = "F2 K A K A";

    [Theory]
    [InlineData("W W W FL FL", Symbol.Float, 5)]    // symbol run (1500) beats the pure Wild run (100)
    [InlineData("W W W A K", Symbol.Wild, 3)]       // pure Wild run (100) beats Ace×4 (60)
    [InlineData("W W W W 10", Symbol.Wild, 4)]      // Wild×4 (500) beats Ten×5 (100)
    [InlineData("W W W W W", Symbol.Wild, 5)]
    [InlineData("W W 10 10 A", Symbol.Ten, 4)]      // W-W-Ten-Ten-x
    [InlineData("W W 10 10 W", Symbol.Ten, 5)]      // a Wild after the run extends it
    [InlineData("W 10 W 10 J", Symbol.Ten, 4)]      // Wilds in between substitute too
    [InlineData("10 W 10 W 10", Symbol.Ten, 5)]
    public void MiddleLine_PaysTheBetterOfWildRunAndSymbolRun(string middle, Symbol symbol, int count)
    {
        var session = Play.InFeature(freeSpins: 5);
        var before = _engine.Snapshot(session);
        var grid = Grids.Parse(1m, FishTop, middle, FishBottom);

        var r = _engine.Apply(session, null, grid); // the Fish mean no net cast

        var win = Assert.Single(r.LineWins);
        Assert.Equal((0, symbol, count), (win.Line, win.Symbol, win.Count));
        Assert.Equal(LineBets(symbol, count) * 0.1m, win.Win);
        // Collect: the two Fish (2 + 2) × number of Wilds × multiplier 1.
        var wilds = middle.Split(' ').Count(c => c == "W");
        Assert.Equal(4m * wilds, r.CollectWin);
        Assert.Equal(win.Win + r.CollectWin, r.TotalWin);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _info.Paylines);
    }

    [Fact]
    public void WildRunAndSymbolRunPayingTheSame_PaysThatAmountOnce()
    {
        // Wild×3 = 100 line bets, Ten×5 = 100 line bets. The engine keeps the Wild run on a tie.
        var r = _engine.Apply(Play.InFeature(freeSpins: 5), null, Grids.Parse(1m, FishTop, "W W W 10 10", FishBottom));

        var win = Assert.Single(r.LineWins);
        Assert.Equal(LineBets(Symbol.Wild, 3) * 0.1m, win.Win);
        Assert.Equal(LineBets(Symbol.Ten, 5) * 0.1m, win.Win);
        Assert.Equal((Symbol.Wild, 3), (win.Symbol, win.Count));
    }

    // ---- Fish: Wild never substitutes for it, so a Fish run must start on reel 1 ----

    [Theory]
    [InlineData("W F5 F5 F5 F5", "")]               // Wild on reel 1 doesn't start a Fish run
    [InlineData("F5 W F5 F5 F5", "")]               // a Wild breaks a Fish run
    [InlineData("J F5 F5 F5 F5", "")]               // Fish run not starting on reel 1
    [InlineData("F5 F5 F5 W 10", "0:Fish:3")]       // a Wild after a Fish run doesn't extend it
    [InlineData("W W W F5 F5", "0:Wild:3 9:Jack:3")] // Fish don't extend a Wild run; line 9 is J-W-W-F
    public void FishRuns_AreNeverHelpedByWilds(string middle, string expectedWins)
    {
        var session = Play.InFeature(freeSpins: 5);
        var before = _engine.Snapshot(session);

        var r = _engine.Apply(session, null, Grids.Parse(1m, Grids.BlankTop, middle, Grids.BlankBottom));

        var expected = expectedWins.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Split(':'))
            .Select(w => (Line: int.Parse(w[0]), Symbol: Enum.Parse<Symbol>(w[1]), Count: int.Parse(w[2])))
            .ToList();
        Assert.Equal(expected, r.LineWins.Select(w => (w.Line, w.Symbol, w.Count)));
        Assert.Equal(expected.Sum(w => LineBets(w.Symbol, w.Count) * 0.1m), r.LineWins.Sum(w => w.Win));
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, _info.Paylines);
        SpinChecks.Money(r);
    }

    // ---- Several lines on one grid ----

    [Theory]
    [MemberData(nameof(TwoBets))]
    public void ThreeLinesPayOnOneGrid_TotalIsTheirExactSum(decimal bet)
    {
        var session = new GameSession("t");
        var grid = Grids.Parse(bet,
            "A A A K Q",          // line 1: Ace×3   = 20 line bets
            "K K K K J",          // line 0: King×4  = 50 line bets
            "10 10 10 10 10");    // line 2: Ten×5   = 100 line bets
        var lineBet = bet / Contract.Lines;

        var r = _engine.Apply(session, bet, grid);

        Assert.Equal(
            [(0, Symbol.King, 4, 50 * lineBet), (1, Symbol.Ace, 3, 20 * lineBet), (2, Symbol.Ten, 5, 100 * lineBet)],
            r.LineWins.Select(w => (w.Line, w.Symbol, w.Count, w.Win)));
        Assert.Equal(170 * lineBet, r.TotalWin);                       // 3.40 at bet 0.20, 34.00 at bet 2
        Assert.Equal(1000m - bet + 170 * lineBet, r.State.Balance);
        SpinChecks.Money(r);
        SpinChecks.LineWins(r, _info.Paylines);
    }

    public static TheoryData<decimal> TwoBets() => [0.20m, 2m];
}
