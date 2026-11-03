namespace MyApp.Tests.Engine;

public class LineWinTests
{
    private readonly SlotEngine _engine = new();

    [Fact]
    public void BaseGameLineWins_FollowPaylines_AndMatchTheGrid()
    {
        var session = Play.RichSession();
        var paylines = _engine.Info().Paylines;
        var wins = 0;

        for (var i = 0; i < 5_000; i++)
        {
            session.ClearFeature();
            var r = _engine.Spin(session, 1m);
            SpinChecks.LineWins(r, paylines);
            wins += r.LineWins.Count;
        }

        Assert.True(wins > 0);
    }

    [Fact]
    public void FreeSpinLineWins_WithWildSubstitution_FollowPaylines_AndMatchTheGrid()
    {
        var session = Play.InFeature(freeSpins: 1_000_000);
        var paylines = _engine.Info().Paylines;
        var wildWins = 0;

        foreach (var (_, r) in Play.Spins(_engine, session, 1m, 5_000))
        {
            SpinChecks.LineWins(r, paylines);
            wildWins += r.LineWins.Count(w => w.Positions.Any(p => r.Grid[p[0]][p[1]].Symbol == Symbol.Wild));
        }

        Assert.True(wildWins > 0, "No line win used a Wild in the sample.");
    }

    [Fact]
    public void LineWin_PaysPaytableTimesLineBet()
    {
        var session = Play.RichSession();
        var paytable = _engine.Info().Paytable;

        foreach (var bet in Contract.Bets)
        {
            var r = Play.Until(_engine, session, bet, x => !x.WasFreeSpin && x.LineWins.Count > 0, maxSpins: 1_000);
            foreach (var w in r.LineWins)
                Assert.Equal(Math.Round(paytable[w.Symbol.ToString()][w.Count - 3] * r.Bet / Contract.Lines, 2), w.Win);
        }
    }
}
