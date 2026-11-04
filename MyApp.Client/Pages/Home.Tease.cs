using MyApp.Client.Game;
using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Base-game Boat tease ("one more boat…"): display timing only, the server result is already known.
// While reels stop (StopReelsAsync, also for the respinning reels of a nudge step), whenever exactly 2 Scatters
// show on the reels that are not spinning and at least one reel still spins, each remaining reel spins longer
// with a glowing frame and the 2 landed Boats pulse. It ends as soon as the count is no longer 2 (the 3rd boat
// landed) or the reels are all stopped. Click / Space (celebrationGate) stops the remaining reels at once.
public partial class Home
{
    private const int TeaseReelMs = 850;         // extra spin per remaining reel
    private const int TeaseCapMs = 3000;         // a whole tease never takes longer than this
    private const int ReducedTeaseReelMs = 350;  // prefers-reduced-motion: shorter, no glow animation

    private readonly bool[] teaseReels = new bool[Symbols.Reels]; // reels getting the anticipation spin
    private bool teasing;
    private bool teaseSkipped;
    private int teaseStepMs;

    /// <summary>Starts, keeps or ends the tease before the next spinning reel stops.</summary>
    private void UpdateTease()
    {
        var boats = 0;
        var stopped = new List<int>();
        for (var r = 0; r < Symbols.Reels; r++)
        {
            if (reelSpinning[r]) continue;
            stopped.Add(r);
            boats += grid[r].Count(c => c.Symbol == Symbol.Scatter);
        }
        var remaining = Symbols.Reels - stopped.Count;

        if (boats != 2 || remaining == 0)
        {
            if (teasing) EndTease();
            return;
        }
        if (teasing || teaseSkipped) return;

        teasing = true;
        teaseStepMs = reducedMotion
            ? ReducedTeaseReelMs
            : Math.Min(TeaseReelMs, TeaseCapMs / remaining);
        for (var r = 0; r < Symbols.Reels; r++) teaseReels[r] = reelSpinning[r];
        winMarks = null;
        traceLines = null;
        boatMarks = WinText.ScatterMarks(grid, stopped);
        ShowMessage("One more boat…", "win");
        Sound.StartLoop(SfxLoop.Tease);
        StateHasChanged();
    }

    private void EndTease()
    {
        if (teasing)
        {
            boatMarks = null;
            Sound.StopLoop(SfxLoop.Tease);
        }
        teasing = false;
        teaseSkipped = false;
        Array.Fill(teaseReels, false);
    }
}
