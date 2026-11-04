using MyApp.Client.Game;
using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Free-spin "net cast" (API_CONTRACT.md, "Net cast"): the reels land on SpinResult.Grid, the fisherman throws
// a net, then the cells in Net.Positions turn into Fish one by one, ending exactly on Net.Grid. Runs inside
// SpinAsync (busy == true); click / Space / Enter skips it via celebrationGate.
public partial class Home
{
    private const int NetBuildUpMs = 750;     // NET CAST! build-up
    private const int BigNetBuildUpMs = 950;  // BIG NET! build-up (with shake)
    private const int NetPopGapMs = 100;      // stagger between fish transforms
    private const int NetSettleMs = 450;      // last fish finishes its flip

    private NetCast? netCast;          // non-null while the net animation is on screen
    private string?[][]? netFx;        // per cell: "react" (fisherman) / "pop" (turned into a Fish)
    private bool netShake;             // BIG NET shakes the machine

    /// <summary>Plays the net cast on the landed grid and returns the transformed grid (Net.Grid).</summary>
    private async Task<Cell[][]> PlayNetCastAsync(NetCast net)
    {
        var big = net.Size == NetSize.Big;

        // 1) Build-up: fishermen react, the net sweeps across, banner.
        winMarks = null;
        boatMarks = null;
        traceLines = null;
        netFx = NewFx();
        for (var r = 0; r < Symbols.Reels; r++)
            for (var row = 0; row < Symbols.Rows; row++)
                if (grid[r][row].Symbol == Symbol.Wild) netFx[r][row] = "react";
        netCast = net;
        netShake = big && !reducedMotion;
        ShowMessage(big ? "BIG NET! The fisherman hauls in a huge catch…" : "NET CAST! The fisherman throws his net…", "win");
        Sound.Play(Sfx.NetCast, big ? 1 : 0);
        StateHasChanged();
        var skipped = await celebrationGate.WaitAsync(reducedMotion ? ReducedPauseMs : big ? BigNetBuildUpMs : NetBuildUpMs);

        // 2) Each netted cell turns into its Fish (with value badge), left to right.
        var cells = net.Positions
            .Where(p => p is { Length: >= 2 } && p[0] is >= 0 and < Symbols.Reels && p[1] is >= 0 and < Symbols.Rows)
            .OrderBy(p => p[0]).ThenBy(p => p[1]);
        foreach (var p in cells)
        {
            if (disposed) break;
            var (reel, row) = (p[0], p[1]);

            var column = (Cell[])grid[reel].Clone();
            column[row] = net.Grid[reel][row];
            grid = (Cell[][])grid.Clone();
            grid[reel] = column;

            var fxColumn = (string?[])netFx[reel].Clone();
            fxColumn[row] = "pop";
            netFx = (string?[][])netFx.Clone();
            netFx[reel] = fxColumn;

            if (!skipped) Sound.Play(Sfx.FishLand);
            StateHasChanged();
            if (!skipped && !reducedMotion) skipped = await celebrationGate.WaitAsync(NetPopGapMs);
        }
        if (!skipped && !reducedMotion) await celebrationGate.WaitAsync(NetSettleMs);

        // 3) End on exactly the server's transformed grid, then the normal win/collect flow continues.
        grid = (Cell[][])net.Grid.Clone();
        ClearNetCast();
        StateHasChanged();
        return grid;
    }

    private void ClearNetCast()
    {
        netCast = null;
        netFx = null;
        netShake = false;
    }

    private static string?[][] NewFx()
    {
        var fx = new string?[Symbols.Reels][];
        for (var r = 0; r < Symbols.Reels; r++) fx[r] = new string?[Symbols.Rows];
        return fx;
    }
}
