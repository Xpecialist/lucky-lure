using Xunit.Abstractions;

namespace MyApp.Tests.Engine;

/// <summary>
/// Monte-Carlo check of the economy. Slow (~1M paid spins), so it carries a trait:
/// run the quick suite with  dotnet test --filter "Category!=Slow".
/// </summary>
[Trait("Category", "Slow")]
public class StatisticalTests(ITestOutputHelper output)
{
    private const int PaidSpins = 1_000_000;

    [Fact]
    public void Rtp_And_TriggerRate_AreInSaneBands()
    {
        var engine = new SlotEngine();
        var session = Play.RichSession(1_000_000_000m);
        decimal staked = 0, won = 0;
        var features = 0;

        for (var i = 0; i < PaidSpins; i++)
        {
            var r = engine.Spin(session, 1m);
            staked += r.Bet;
            won += r.TotalWin;
            if (r.FreeSpinsAwarded == 0)
                continue;

            features++;
            while (session.FreeSpinsRemaining > 0)
                won += engine.Spin(session, null).TotalWin;
        }

        var rtp = (double)(won / staked);
        var oneIn = (double)PaidSpins / Math.Max(1, features);
        output.WriteLine($"RTP {rtp:P2}, feature 1 in {oneIn:0.0} paid spins ({features:N0} features).");

        Assert.InRange(rtp, 0.90, 1.02);
        Assert.InRange(oneIn, 40, 62);
    }
}
