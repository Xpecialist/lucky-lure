namespace MyApp.Tests.Engine;

/// <summary>
/// Every paytable entry, exactly: a run of 3 / 4 / 5 of a symbol on a payline pays paytable × line bet.
/// The expected pays are read from engine.Info() (what the client shows), so the test checks that the
/// evaluator and the published paytable agree.
/// </summary>
public class PaytableTests
{
    private static readonly SlotEngine Engine = new(new CryptoRandomSource()); // RNG unused by Apply
    private static readonly GameInfo Info = Engine.Info();

    // A V-shaped line, so the test is not only about straight rows.
    private const int ChosenLine = 3; // [0, 1, 2, 1, 0]

    /// <summary>Every paying symbol × 3/4/5 of a kind × two bets: 11 × 3 × 2 = 66 cases.</summary>
    public static TheoryData<Symbol, int, decimal> PaytableCases()
    {
        var data = new TheoryData<Symbol, int, decimal>();
        foreach (var name in Info.Paytable.Keys)
            foreach (var count in new[] { 3, 4, 5 })
                foreach (var bet in new[] { 0.10m, 2m })
                    data.Add(Enum.Parse<Symbol>(name), count, bet);
        return data;
    }

    [Theory]
    [MemberData(nameof(PaytableCases))]
    public void RunOnAPayline_PaysPaytableTimesLineBet(Symbol symbol, int count, decimal bet)
    {
        // Arrange: Wilds only land in free spins, so the Wild row is played as a free spin.
        var session = symbol == Symbol.Wild ? Play.InFeature(freeSpins: 5, freeSpinBet: bet) : new GameSession("t");
        var before = Engine.Snapshot(session);
        var grid = RunGrid(ChosenLine, symbol, count, bet);
        var expected = Math.Round(Info.Paytable[symbol.ToString()][count - 3] * bet / Contract.Lines, 2,
            MidpointRounding.AwayFromZero);

        // Act
        var r = Engine.Apply(session, bet, grid, net: null);

        // Assert
        var win = Assert.Single(r.LineWins, w => w.Line == ChosenLine);
        Assert.Equal((symbol, count, expected), (win.Symbol, win.Count, win.Win));
        if (symbol != Symbol.Wild)
            Assert.Single(r.LineWins); // the filler guarantees no other line pays (Wilds substitute, so they can)
        Assert.Equal(r.LineWins.Sum(w => w.Win) + r.CollectWin, r.TotalWin);
        SpinChecks.Money(r);
        SpinChecks.Transition(before, bet, r);
        SpinChecks.LineWins(r, Info.Paylines);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void FiveAcesOnEachPayline_PayOnThatLineOnly(int line)
    {
        var r = Engine.Apply(new GameSession("t"), 1m, RunGrid(line, Symbol.Ace, 5, 1m));

        var win = Assert.Single(r.LineWins);
        Assert.Equal((line, Symbol.Ace, 5), (win.Line, win.Symbol, win.Count));
        Assert.Equal(Info.Paytable["Ace"][2] * 0.1m, win.Win);
        Assert.Equal(Info.Paylines[line].Select((row, reel) => new[] { reel, row }), win.Positions);
    }

    /// <summary>
    /// <paramref name="count"/> × <paramref name="symbol"/> from reel 1 along <paramref name="line"/>; every other
    /// cell is a filler card, one per reel, cycling through three cards that differ from the symbol.
    /// Neighbouring reels never share a filler, and no two paylines share their first three cells, so (without
    /// Wilds) no other line can pay and the run stops exactly at <paramref name="count"/>.
    /// </summary>
    private static Cell[][] RunGrid(int line, Symbol symbol, int count, decimal bet)
    {
        Symbol[] fillers = [.. new[] { Symbol.Ten, Symbol.Jack, Symbol.Queen, Symbol.King }.Where(s => s != symbol).Take(3)];
        var rows = Info.Paylines[line];
        var grid = new Cell[Contract.Reels][];
        for (var reel = 0; reel < Contract.Reels; reel++)
        {
            grid[reel] = new Cell[Contract.Rows];
            for (var row = 0; row < Contract.Rows; row++)
            {
                var s = reel < count && row == rows[reel] ? symbol : fillers[reel % 3];
                grid[reel][row] = new Cell(s, s == Symbol.Fish ? 2 * bet : null);
            }
        }
        return grid;
    }
}
