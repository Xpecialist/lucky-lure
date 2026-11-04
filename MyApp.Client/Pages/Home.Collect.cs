using Microsoft.JSInterop;
using MyApp.Client.Game;
using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Free-spin collect (API_CONTRACT.md: collectWin = fish total × wilds × multiplier). After the reels land (and
// after a net cast), each Fish's value flies to the first Fisherman while a running total counts up on him;
// then the factor ("×2 ×3") and the final CollectWin. Finally every Fisherman sparkles and a copy flies to the
// fisherman counter bar, whose slots light up as they arrive. Runs inside SpinAsync (busy == true), before the
// win celebration and any RETRIGGER overlay. Click / Space / Enter skips to the end (celebrationGate).
public partial class Home
{
    private const int FishFlightMs = 240;    // one value's flight to the fisherman
    private const int FishGapMs = 220;       // stagger between fish (the total ticks up as each lands)
    private const int FactorMs = 550;        // "12.00 ×2 ×3" before the final number
    private const int FinalHoldMs = 650;     // final CollectWin on the fisherman
    private const int SparkMs = 450;         // sparkle burst on the fisherman cell
    private const int SparkFlightMs = 650;   // sparkle copy's flight to the counter bar
    private const int SparkGapMs = 250;      // stagger between several fishermen

    private sealed record CollectModel(int Reel, int Row, decimal Amount, string? Factor, bool Final);

    private CollectModel? collect;        // counter on the fisherman (null = hidden)
    private string?[][]? collectFx;       // per cell: "collector" / "gathered" (value flown) / "sparkle"
    private int? wildsShown;              // counter-bar override while sparkles are in the air
    private bool collecting;              // shows the skip layer
    private IJSObjectReference? fxModule; // js/fx.js (flights)

    private async Task PlayCollectAsync(SpinResult result, Cell[][] finalGrid, GameState prevState)
    {
        var wilds = Positions(finalGrid, Symbol.Wild);
        if (wilds.Count == 0) return;

        var (wr, wrow) = wilds[0];
        var fish = result.CollectWin > 0
            ? Positions(finalGrid, Symbol.Fish).Where(p => finalGrid[p.Reel][p.Row].Value is > 0).ToList()
            : [];

        collecting = true;
        winMarks = null;
        boatMarks = null;
        traceLines = null;
        wildsShown = prevState.WildsCollected;
        collectFx = NewFx();
        foreach (var (r, row) in wilds) collectFx[r][row] = "collector";
        var skipped = false;
        var animate = !reducedMotion && fxModule is not null;

        try
        {
            // 1) Fish values fly one by one to the first fisherman; his counter adds them up.
            if (fish.Count > 0)
            {
                ShowMessage(wilds.Count > 1 ? "The Fishermen reel in the catch…" : "The Fisherman reels in the catch…", "win");
                var sum = 0m;
                collect = new CollectModel(wr, wrow, 0m, null, false);
                StateHasChanged();

                var n = 0;
                foreach (var (r, row) in fish)
                {
                    var value = finalGrid[r][row].Value ?? 0m;
                    if (animate)
                    {
                        Sound.Play(Sfx.Pluck, n++); // climbs a step per fish
                        await FlyAsync("flyValue", r, row, wr, wrow, Fmt.Money(value), FishFlightMs);
                        MarkFx(r, row, "gathered");
                        StateHasChanged();
                        skipped = await celebrationGate.WaitAsync(FishGapMs);
                        if (skipped || disposed) break;
                    }
                    sum += value;
                    collect = collect with { Amount = sum };
                    StateHasChanged();
                }

                // 2) The factor (fishermen × multiplier), then exactly the server's CollectWin.
                if (!skipped && !disposed)
                {
                    var factor = FactorText(wilds.Count, prevState.Multiplier);
                    if (factor is not null && animate)
                    {
                        collect = collect with { Factor = factor };
                        StateHasChanged();
                        skipped = await celebrationGate.WaitAsync(FactorMs);
                    }
                }
                collect = collect with { Amount = result.CollectWin, Factor = null, Final = true };
                if (!skipped) Sound.Play(Sfx.Kaching);
                StateHasChanged();
                if (!skipped && !disposed) skipped = await celebrationGate.WaitAsync(reducedMotion ? ReducedPauseMs : FinalHoldMs);
            }

            // 3) Every fisherman sparkles; a copy flies to the counter bar and fills the next slot on arrival.
            var slots = SlotCount;
            if (!skipped && !disposed && animate)
            {
                foreach (var (r, row) in wilds) MarkFx(r, row, "sparkle");
                ShowMessage(wilds.Count > 1 ? $"{wilds.Count} Fishermen join the crew!" : "A Fisherman joins the crew!", "win");
                StateHasChanged();
                skipped = await celebrationGate.WaitAsync(SparkMs);

                for (var i = 0; i < wilds.Count && !skipped && !disposed; i++)
                {
                    var slot = Math.Min(prevState.WildsCollected + i, slots - 1);
                    Sound.Play(Sfx.Twinkle);
                    await FlyAsync("flySpark", wilds[i].Reel, wilds[i].Row, slot, SparkFlightMs);
                    if (i < wilds.Count - 1)
                    {
                        _ = LightSlotLaterAsync(prevState.WildsCollected + i + 1, SparkFlightMs); // lands while the next flies
                        skipped = await celebrationGate.WaitAsync(SparkGapMs);
                    }
                    else
                    {
                        skipped = await celebrationGate.WaitAsync(SparkFlightMs);
                    }
                }
                if (!skipped && !disposed)
                {
                    wildsShown = prevState.WildsCollected + wilds.Count; // the last slot pops (and a plate, if reached)
                    Sound.Play(Sfx.Slot);
                    StateHasChanged();
                    await celebrationGate.WaitAsync(450);
                }
            }
        }
        finally
        {
            if (skipped && fxModule is not null)
            {
                try { await fxModule.InvokeVoidAsync("clearFlights"); }
                catch (JSException) { }
            }
            collect = null;
            collectFx = null;
            wildsShown = null;   // the caller's SetState(result.State) shows the final count
            collecting = false;
        }
    }

    private int SlotCount =>
        Math.Max(1, info?.WildsPerRetrigger ?? 4) * Math.Max(1, (info?.Multipliers?.Count ?? 4) - 1);

    /// <summary>Lights the counter bar up to <paramref name="count"/> once that sparkle has landed.</summary>
    private async Task LightSlotLaterAsync(int count, int delayMs)
    {
        await Task.Delay(Math.Max(0, delayMs));
        if (!collecting || wildsShown is not int shown || count <= shown) return;
        wildsShown = count;
        Sound.Play(Sfx.Slot);
        await InvokeAsync(StateHasChanged);
    }

    private async Task FlyAsync(string fn, params object[] args)
    {
        if (fxModule is null) return;
        try { await fxModule.InvokeVoidAsync(fn, args); }
        catch (JSException) { /* flights are decoration only */ }
    }

    private void MarkFx(int reel, int row, string fx)
    {
        if (collectFx is null) return;
        var column = (string?[])collectFx[reel].Clone();
        column[row] = fx;
        collectFx = (string?[][])collectFx.Clone();
        collectFx[reel] = column;
    }

    /// <summary>"×2 ×3" for 2 fishermen at ×3, "×3" for one at ×3, null when the sum is already the win.</summary>
    private static string? FactorText(int wilds, int multiplier)
    {
        var parts = new List<string>();
        if (wilds > 1) parts.Add($"×{wilds}");
        if (multiplier > 1) parts.Add($"×{multiplier}");
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static List<(int Reel, int Row)> Positions(Cell[][] grid, Symbol symbol)
    {
        var list = new List<(int, int)>();
        for (var r = 0; r < grid.Length && r < Symbols.Reels; r++)
            for (var row = 0; row < grid[r].Length && row < Symbols.Rows; row++)
                if (grid[r][row].Symbol == symbol) list.Add((r, row));
        return list;
    }
}
