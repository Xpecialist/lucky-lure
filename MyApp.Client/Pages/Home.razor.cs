using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MyApp.Client.Game;
using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

public partial class Home : IAsyncDisposable
{
    private const int MinSpinMs = 350;      // reels always spin at least this long
    private const int StopGapMs = 130;      // left-to-right stop stagger
    private const int LandMs = 360;         // matches the reel-land CSS animation

    private sealed record OverlayModel(string Title, string Text, string Kind);

    [Inject] private GameClient Client { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private GameInfo? info;
    private GameState? state;
    private int betIndex;

    private Cell[][] grid = Symbols.RandomGrid(1m);
    private readonly bool[] reelSpinning = new bool[Symbols.Reels];
    private readonly bool[] heldReels = new bool[Symbols.Reels]; // nudge respin (Home.Respins.cs)
    private decimal fillerBet = 1m;
    private bool fillerFree;
    private bool[][]? winMarks;
    private bool[][]? boatMarks;                 // celebrating Scatters (trigger / nudge tease)
    private IReadOnlyList<LineWin>? traceLines;  // paylines drawn over the reels
    private int traceId;                         // new id = trace animation replays

    private bool busy;          // from SPIN until the result (and any overlays) are done
    private bool resetting;
    private bool autoOn;
    private bool reducedMotion;
    private decimal displayBalance;
    private decimal winAmount;
    private string message = "Loading…";
    private string? messageKind;
    private decimal? featureWinOverride;

    private OverlayModel? overlay;
    private readonly SkipGate overlayGate = new();      // overlays wait for click / Space / Enter
    private readonly SkipGate celebrationGate = new();  // big/mega win banner, skippable
    private CancellationTokenSource? nextSpin;

    private IJSObjectReference? keys;
    private DotNetObjectReference<Home>? selfRef;
    private bool disposed;

    private bool Ready => info is not null && state is not null;
    private bool InFeature => state is { FreeSpinsRemaining: > 0 };
    private bool ShowBanner => Ready && (InFeature || featureWinOverride is not null);
    private bool CanChangeBet => Ready && !busy && !InFeature;
    private int BetCount => info?.Bets.Count ?? 0;

    private decimal CurrentBet =>
        InFeature && state!.FreeSpinBet > 0 ? state.FreeSpinBet
        : info is { Bets.Count: > 0 } ? info.Bets[Math.Clamp(betIndex, 0, info.Bets.Count - 1)]
        : 1m;

    // ---------- lifecycle ----------

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var infoTask = Client.GetInfoAsync();
            var stateTask = Client.GetStateAsync();
            await Task.WhenAll(infoTask, stateTask);

            info = infoTask.Result;
            if (info.Bets is not { Count: > 0 }) info = info with { Bets = [1m] };
            SetState(stateTask.Result);

            var preferred = InFeature ? state!.FreeSpinBet : 1m;
            betIndex = IndexOfBet(preferred);
            if (betIndex < 0) betIndex = Math.Max(0, IndexOfBet(1m));

            grid = Symbols.RandomGrid(CurrentBet);
            ShowMessage(InFeature
                ? $"Free spins in progress — {state!.FreeSpinsRemaining} left. Press SPIN to continue."
                : "Press SPIN (or Space) to cast your line.", InFeature ? "win" : null);
        }
        catch (ApiException ex)
        {
            ShowMessage($"Could not load the game: {ex.Message} Reload the page to retry.", "error");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            keys = await JS.InvokeAsync<IJSObjectReference>("import", "./js/keys.js");
            selfRef = DotNetObjectReference.Create(this);
            await keys.InvokeVoidAsync("register", selfRef);
            reducedMotion = await keys.InvokeAsync<bool>("prefersReducedMotion");
        }
        catch (JSException)
        {
            // Keyboard shortcuts are a convenience; the buttons still work.
        }
        try
        {
            fxModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/fx.js");
        }
        catch (JSException)
        {
            // Collect flights are decoration; without them the totals still count up.
        }
    }

    [JSInvokable]
    public Task OnSpaceKey() => SpinAsync();

    /// <summary>Space/Enter while an overlay or a win celebration is showing.</summary>
    [JSInvokable]
    public void OnDismissKey()
    {
        if (overlay is not null) overlayGate.Skip();
        else celebrationGate.Skip();
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        CancelNextSpin();
        overlayGate.Skip();
        celebrationGate.Skip();
        if (keys is not null)
        {
            try
            {
                await keys.InvokeVoidAsync("unregister");
                await keys.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Page is going away; nothing to clean up.
            }
        }
        selfRef?.Dispose();
        if (fxModule is not null)
        {
            try { await fxModule.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }

    // ---------- spin ----------

    private async Task SpinAsync()
    {
        if (busy || disposed || !Ready || overlay is not null) return;
        CancelNextSpin();

        var prevState = state!;
        var free = InFeature;
        var bet = CurrentBet;
        if (!free && prevState.Balance < bet)
        {
            StopAuto();
            ShowMessage($"Not enough balance for a {Fmt.Money(bet)} bet. Lower the bet or reset your balance.", "error");
            StateHasChanged();
            return;
        }

        // 1) Start all reels spinning right now (pure CSS loop) and show the optimistic balance.
        busy = true;
        winMarks = null;
        boatMarks = null;
        traceLines = null;
        winAmount = 0;
        featureWinOverride = null;
        if (!free) displayBalance = prevState.Balance - bet;
        ShowMessage(free
            ? $"Free spin {prevState.FreeSpinsTotal - prevState.FreeSpinsRemaining + 1} of {prevState.FreeSpinsTotal}…"
            : "Casting…");
        fillerBet = bet;
        fillerFree = free;
        Array.Fill(reelSpinning, true);
        StateHasChanged();

        // 2) Fire the request in parallel; the render above is flushed as soon as we await.
        var request = Client.SpinAsync(bet);
        var minSpin = Task.Delay(reducedMotion ? 0 : MinSpinMs);

        var prevGrid = grid;
        SpinResult result;
        try
        {
            result = await request;
        }
        catch (ApiException ex)
        {
            await minSpin;
            await StopReelsAsync(prevGrid);
            await FailSpinAsync(ex.Message, refresh: !ex.IsNetwork);
            return;
        }

        try
        {
            // 3) Stop reels left to right on the server's grid.
            await minSpin;
            await StopReelsAsync(result.Grid);
            if (disposed) return;

            // 3b) Base-game nudge respins (if any) before the final result.
            var (finalGrid, finalLines) = (result.Grid, result.LineWins);

            // 3a) Free-spin net cast: cells turn into Fish; wins/collect are on the transformed grid.
            if (result.Net is { } net)
            {
                finalGrid = await PlayNetCastAsync(net);
                if (disposed) return;
            }
            if (result.Respins.Count > 0)
            {
                (finalGrid, finalLines) = await PlayRespinsAsync(result);
                if (disposed) return;
            }

            // 3c) Free spins: fish values fly to the fisherman, then fishermen fly to the counter bar.
            if (result.WasFreeSpin && result.WildCount > 0)
            {
                await PlayCollectAsync(result, finalGrid, prevState);
                if (disposed) return;
            }

            SetState(result.State);
            winAmount = result.TotalWin;
            winMarks = WinText.Marks(result, finalGrid, finalLines, final: true);
            ShowTrace(finalLines);
            ShowMessage(WinText.Describe(result, finalLines, final: true), result.TotalWin > 0 ? "win" : null);
            if (result.FeatureEnded)
                featureWinOverride = Math.Max(Math.Round(result.State.FreeSpinWin, 2), Math.Round(prevState.FreeSpinWin + result.TotalWin, 2));
            StateHasChanged();

            // 4) Celebrate, then the feature overlays. Overlays never auto-dismiss: they wait for the player.
            var celebrated = await CelebrateWinAsync(result);
            if (result.FreeSpinsAwarded > 0)
            {
                await CelebrateBoatsAsync(finalGrid, result.ScatterCount);
                await ShowOverlayAsync("FREE SPINS!", $"{result.FreeSpinsAwarded} free spins", "trigger");
            }
            // Let a retriggering / last free spin finish on screen (wins, collect) before its overlay.
            if ((result.Retriggered || result.FeatureEnded) && !celebrated) await PauseAsync(FeatureSettleMs);
            if (result.Retriggered)
            {
                var added = result.State.FreeSpinsTotal - prevState.FreeSpinsTotal;
                if (added <= 0) added = 10;
                await ShowOverlayAsync("RETRIGGER!", $"+{added} spins — now ×{result.State.Multiplier}", "retrigger");
            }
            if (result.FeatureEnded)
                await ShowOverlayAsync("Feature complete", $"You won {Fmt.Money(featureWinOverride ?? 0)}", "end");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            await FailSpinAsync("Something went wrong.", refresh: true);
            return;
        }

        busy = false;
        featureWinOverride = null;
        StateHasChanged();
        ScheduleNext();
    }

    /// <summary>Stops the reels that are spinning, left to right, on <paramref name="target"/>.</summary>
    private async Task StopReelsAsync(Cell[][] target)
    {
        grid = (Cell[][])grid.Clone(); // keep the caller's previous grid intact
        var first = true;
        for (var r = 0; r < Symbols.Reels; r++)
        {
            if (!reelSpinning[r]) continue;
            if (!first && !reducedMotion) await Task.Delay(StopGapMs);
            first = false;
            grid[r] = target[r];
            reelSpinning[r] = false;
            StateHasChanged();
        }
        if (!reducedMotion) await Task.Delay(LandMs);
    }

    private async Task FailSpinAsync(string error, bool refresh)
    {
        Array.Fill(reelSpinning, false);
        Array.Fill(heldReels, false);
        boatMarks = null;
        traceLines = null;
        celebration = null;
        ClearNetCast();
        collect = null;
        collectFx = null;
        wildsShown = null;
        collecting = false;
        busy = false;
        featureWinOverride = null;
        CancelNextSpin();
        StopAuto();
        if (state is not null) displayBalance = state.Balance;
        ShowMessage(error, "error");
        StateHasChanged();
        if (refresh) await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            SetState(await Client.GetStateAsync());
            StateHasChanged();
        }
        catch (ApiException)
        {
            // Keep showing the original error.
        }
    }

    // ---------- auto-play & scheduling ----------

    private void ScheduleNext()
    {
        if (state is null || disposed) return;
        if (InFeature)
        {
            ScheduleSpin(reducedMotion ? 500 : 900);
            return;
        }
        if (!autoOn) return;
        if (state.Balance < CurrentBet)
        {
            StopAuto();
            ShowMessage($"{message} — Auto-spin stopped: balance too low.", "error");
            StateHasChanged();
            return;
        }
        ScheduleSpin(reducedMotion ? 500 : 1000);
    }

    private void ScheduleSpin(int delayMs)
    {
        CancelNextSpin();
        nextSpin = new CancellationTokenSource();
        _ = SpinLaterAsync(delayMs, nextSpin.Token);
    }

    private async Task SpinLaterAsync(int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await InvokeAsync(SpinAsync);
    }

    private void CancelNextSpin()
    {
        nextSpin?.Cancel();
        nextSpin?.Dispose();
        nextSpin = null;
    }

    private async Task ToggleAutoAsync()
    {
        autoOn = !autoOn;
        if (autoOn)
        {

            if (!busy && overlay is null && nextSpin is null) await SpinAsync();
        }
        else if (!InFeature)
        {
            CancelNextSpin();
        }
    }

    private void StopAuto() => autoOn = false;

    // ---------- overlays ----------

    /// <summary>Shows a feature overlay and waits until the player dismisses it (click, Space or Enter).</summary>
    private async Task ShowOverlayAsync(string title, string text, string kind)
    {
        overlay = new OverlayModel(title, text, kind);
        StateHasChanged();
        await overlayGate.WaitAsync();
        overlay = null;
        StateHasChanged();
    }
    // ---------- bet & reset ----------

    private void ChangeBet(int delta)
    {
        if (!CanChangeBet) return;
        var i = betIndex + delta;
        if (i >= 0 && i < BetCount) betIndex = i;
    }

    private async Task ResetAsync()
    {
        if (busy || resetting) return;
        CancelNextSpin();
        StopAuto();
        resetting = true;
        try
        {
            SetState(await Client.ResetAsync());
            winAmount = 0;
            winMarks = null;
            boatMarks = null;
            traceLines = null;
            featureWinOverride = null;
            ShowMessage($"Balance reset to {Fmt.Money(state!.Balance)}. Good luck!");
        }
        catch (ApiException ex)
        {
            ShowMessage(ex.Message, "error");
        }
        finally
        {
            resetting = false;
        }
    }

    // ---------- helpers ----------

    private void SetState(GameState s)
    {
        state = s;
        displayBalance = s.Balance;
    }

    private void ShowMessage(string text, string? kind = null)
    {
        message = text;
        messageKind = kind;
    }

    private int IndexOfBet(decimal bet)
    {
        if (info is null) return -1;
        for (var i = 0; i < info.Bets.Count; i++)
            if (info.Bets[i] == bet) return i;
        return -1;
    }
}
