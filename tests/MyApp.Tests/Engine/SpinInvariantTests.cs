namespace MyApp.Tests.Engine;

/// <summary>
/// Property-style tests: the grid is random, so instead of checking one expected outcome we play
/// many spins and check rules that must hold for every one of them (see <see cref="SpinChecks"/>).
/// </summary>
public class SpinInvariantTests
{
    private const int SpinsPerBet = 1_500;
    private const int AccountingSpins = 20_000;

    private readonly SlotEngine _engine = new(new CryptoRandomSource());

    [Theory]
    [MemberData(nameof(Contract.AllBets), MemberType = typeof(Contract))]
    public void BaseGameGrids_FollowSymbolRules(decimal bet)
    {
        var session = Play.RichSession();

        // Calling ClearFeature keeps every spin a base-game spin even when one triggers the feature.
        for (var i = 0; i < SpinsPerBet; i++)
        {
            session.ClearFeature();
            var r = _engine.Spin(session, bet);

            Assert.False(r.WasFreeSpin);
            SpinChecks.Grid(r);
        }
    }

    [Theory]
    [MemberData(nameof(Contract.AllBets), MemberType = typeof(Contract))]
    public void FreeSpinGrids_FollowSymbolRules(decimal bet)
    {
        var session = Play.InFeature(freeSpins: 1_000_000, freeSpinBet: bet);
        var nets = 0;

        foreach (var (_, r) in Play.Spins(_engine, session, bet, SpinsPerBet))
        {
            Assert.True(r.WasFreeSpin);
            SpinChecks.Grid(r); // includes the net cast rule
            if (r.Net is not null) nets++;
        }

        // ~40 expected per 1,500 free spins; zero would mean the net invariants were never exercised.
        Assert.True(nets > 0, "No net cast in the sample.");
    }

    [Fact]
    public void BaseGame_NeverCastsANet()
    {
        var session = Play.RichSession();
        for (var i = 0; i < SpinsPerBet; i++)
        {
            session.ClearFeature();
            Assert.Null(_engine.Spin(session, 1m).Net);
        }
    }

    [Fact]
    public void ManySpins_AccountingAndStateTransitionsHold()
    {
        var session = Play.RichSession();
        var paylines = _engine.Info().Paylines;
        int paid = 0, free = 0, triggers = 0;

        for (var i = 0; i < AccountingSpins; i++)
        {
            var bet = Contract.Bets[i % Contract.Bets.Length]; // ignored by the engine during free spins
            var before = _engine.Snapshot(session);
            var r = _engine.Spin(session, bet);

            SpinChecks.Money(r);
            SpinChecks.Transition(before, bet, r);
            SpinChecks.LineWins(r, paylines);
            Assert.Equal(r.State, _engine.Snapshot(session)); // the returned state is the session's state

            if (r.WasFreeSpin) free++; else paid++;
            if (r.FreeSpinsAwarded > 0) triggers++;
        }

        // Sanity: the run actually exercised both modes.
        Assert.True(triggers > 0, "No feature was triggered in the sample.");
        Assert.True(free > 0 && paid > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NudgeRespins_FollowTheRules_IncludingTheCap(int respins)
    {
        // Makes sure the invariants above really see nudges: find a spin with this many respins and check it all.
        var session = Play.RichSession();
        var paylines = _engine.Info().Paylines;

        var before = _engine.Snapshot(session);
        var r = Play.Until(_engine, session, 1m, x =>
        {
            var hit = !x.WasFreeSpin && x.Respins.Count == respins;
            if (!hit)
            {
                session.ClearFeature(); // stay in the base game
                before = _engine.Snapshot(session);
            }
            return hit;
        }, maxSpins: 200_000);

        SpinChecks.Grid(r);
        SpinChecks.Money(r);
        SpinChecks.Transition(before, 1m, r);
        SpinChecks.LineWins(r, paylines);
    }

    [Fact]
    public void PlayingFromStartingBalance_BalanceNeverGoesNegative()
    {
        var session = new GameSession("t");

        for (var i = 0; i < 5_000; i++)
        {
            var inFeature = session.FreeSpinsRemaining > 0;
            var affordable = Contract.Bets.Where(b => b <= session.Balance).ToList();
            if (!inFeature && affordable.Count == 0)
                break; // broke: no bet is valid any more

            var bet = inFeature ? 0m : affordable[^1]; // always bet the most we can
            var r = _engine.Spin(session, bet);

            Assert.True(r.State.Balance >= 0, $"Balance went negative: {r.State.Balance}.");
        }
    }
}
