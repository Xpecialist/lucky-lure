using MyApp.Client.Game;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Base-game "nudge respin" sequence (API_CONTRACT.md, "Nudge respin"). Runs inside SpinAsync while
// busy == true, so Space / auto-spin cannot start another spin until it (and any overlays) finish.
public partial class Home
{
    private const int StagePauseMs = 700;    // show a stage's line wins before the next step
    private const int TeasePauseMs = 650;    // "2 Boats — nudge respin!" with the Scatters glowing
    private const int ReducedPauseMs = 250;  // both pauses with prefers-reduced-motion

    /// <summary>
    /// Plays every respin step after the original grid has landed and returns the final grid and its
    /// line wins (scatter pay / collect for the final grid are shown by the caller).
    /// </summary>
    private async Task<(Cell[][] Grid, IReadOnlyList<LineWin> LineWins)> PlayRespinsAsync(SpinResult result)
    {
        var steps = result.Respins;
        var won = result.LineWins.Sum(w => w.Win);
        await ShowStageAsync(result, result.Grid, result.LineWins, won);

        for (var i = 0; i < steps.Count && !disposed; i++)
        {
            var step = steps[i];

            // Tease: glow the Scatters on the reels about to be held.
            foreach (var r in step.HeldReels) heldReels[r] = true;
            winMarks = null;
            traceLines = null;
            boatMarks = WinText.ScatterMarks(grid, step.HeldReels);
            ShowMessage(i == 0
                ? $"{step.HeldReels.Length} Boats — nudge respin!"
                : $"{step.HeldReels.Length} Boats again — another nudge respin!", "win");
            StateHasChanged();
            await PauseAsync(TeasePauseMs);

            // Nudge the held reels down one cell (CSS) and respin the others like a normal spin.
            boatMarks = null;
            grid = (Cell[][])grid.Clone();
            for (var r = 0; r < Symbols.Reels; r++)
            {
                if (heldReels[r]) grid[r] = step.Grid[r];
                else reelSpinning[r] = true;
            }
            fillerFree = false;
            StateHasChanged();
            await Task.Delay(reducedMotion ? 0 : MinSpinMs);
            await StopReelsAsync(step.Grid);

            Array.Fill(heldReels, false);
            won += step.LineWins.Sum(w => w.Win);
            if (i < steps.Count - 1) await ShowStageAsync(result, step.Grid, step.LineWins, won);
        }

        var last = steps[^1];
        return (last.Grid, last.LineWins);
    }

    /// <summary>Highlights one intermediate grid's line wins and the running win total, then pauses.</summary>
    private async Task ShowStageAsync(SpinResult result, Cell[][] stageGrid, IReadOnlyList<LineWin> lineWins, decimal wonSoFar)
    {
        winAmount = wonSoFar;
        winMarks = WinText.Marks(result, stageGrid, lineWins, final: false);
        ShowTrace(lineWins);
        ShowMessage(WinText.Describe(result, lineWins, final: false), lineWins.Count > 0 ? "win" : null);
        StateHasChanged();
        await PauseAsync(StagePauseMs);
    }

    private Task PauseAsync(int ms) => Task.Delay(reducedMotion ? ReducedPauseMs : ms);
}
