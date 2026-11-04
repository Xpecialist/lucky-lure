using MyApp.Client.Game;
using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Win celebrations, scaled to the win (WinTiers): small wins = bouncing cells + payline trace (never block),
// BIG / MEGA = banner + rolling counter (+ shower), skippable with click/Space. Boats celebrate before FREE SPINS.
public partial class Home
{
    private const int FeatureSettleMs = 1300;  // free spin result stays on screen before RETRIGGER / complete
    private const int BoatPartyMs = 1300;      // "3 BOATS!" before the FREE SPINS overlay
    private static readonly int[] AllReels = [0, 1, 2, 3, 4];

    private sealed record CelebrationModel(WinTier Tier, decimal Amount, int RollMs);

    private CelebrationModel? celebration;

    /// <summary>Shows the BIG/MEGA WIN banner when the win qualifies; returns whether it did.</summary>
    private async Task<bool> CelebrateWinAsync(SpinResult result)
    {
        var tier = WinTiers.For(result.TotalWin, result.Bet);
        if (tier < WinTier.Big) return false;

        var rollMs = tier == WinTier.Mega ? 2600 : 1600;
        celebration = new CelebrationModel(tier, result.TotalWin, rollMs);
        Sound.Play(tier == WinTier.Mega ? Sfx.MegaWin : Sfx.BigWin);
        if (!reducedMotion) Sound.Play(Sfx.RollTicks, rollMs);
        StateHasChanged();
        await celebrationGate.WaitAsync(reducedMotion ? 1500 : rollMs + 1400);
        celebration = null;
        StateHasChanged();
        return true;
    }

    /// <summary>The Scatters that triggered free spins glow and rock, with a "3 BOATS!" message.</summary>
    private async Task CelebrateBoatsAsync(Cell[][] finalGrid, int count)
    {
        winMarks = null;
        traceLines = null;
        boatMarks = WinText.ScatterMarks(finalGrid, AllReels);
        ShowMessage($"{count} BOATS! The fish are biting…", "win");
        Sound.Play(Sfx.Boats3);
        StateHasChanged();
        await PauseAsync(BoatPartyMs);
    }

    /// <summary>Draws (and re-animates) the payline trace for these line wins.</summary>
    private void ShowTrace(IReadOnlyList<LineWin> lineWins)
    {
        traceLines = lineWins.Count > 0 ? lineWins : null;
        traceId++;
    }
}
