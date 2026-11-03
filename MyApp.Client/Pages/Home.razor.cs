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
    private const int LandMs = 300;         // matches the reel-land CSS animation
    private const int OverlayAutoMs = 4000; // auto-dismiss during auto-play

    private sealed record OverlayModel(string Title, string Text, string Kind);

    [Inject] private GameClient Client { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private GameInfo? info;
    private GameState? state;
    private int betIndex;

    private Cell[][] grid = Symbols.RandomGrid(1m);
    private readonly bool[] reelSpinning = new bool[Symbols.Reels];
    private int spinId;
    private decimal fillerBet = 1m;
    private bool fillerFree;
    private bool[][]? winMarks;

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
    private TaskCompletionSource? overlayDone;
    private CancellationTokenSource? overlayTimer;
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
    }

    [JSInvokable]
    public Task OnSpaceKey() => SpinAsync();

    [JSInvokable]
    public void OnOverlayKey() => DismissOverlay();

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        CancelNextSpin();
        CancelOverlayTimer();
        overlayDone?.TrySetResult();
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
        winAmount = 0;
        featureWinOverride = null;
        if (!free) displayBalance = prevState.Balance - bet;
        ShowMessage(free
            ? $"Free spin {prevState.FreeSpinsTotal - prevState.FreeSpinsRemaining + 1} of {prevState.FreeSpinsTotal}…"
            : "Casting…");
        fillerBet = bet;
        fillerFree = free;
        spinId++;
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

            SetState(result.State);
            winAmount = result.TotalWin;
            winMarks = WinText.Marks(result);
            ShowMessage(WinText.Describe(result), result.TotalWin > 0 ? "win" : null);
            if (result.FeatureEnded)
                featureWinOverride = Math.Max(Math.Round(result.State.FreeSpinWin, 2), Math.Round(prevState.FreeSpinWin + result.TotalWin, 2));
            StateHasChanged();

            // 4) Feature overlays, in order.
            if (result.FreeSpinsAwarded > 0)
                await ShowOverlayAsync("FREE SPINS!", $"{result.FreeSpinsAwarded} spins", "trigger");
            if (result.Retriggered)
            {
                var added = result.State.FreeSpinsTotal - prevState.FreeSpinsTotal;
                if (added <= 0) added = 10;
                await ShowOverlayAsync("RETRIGGER!", $"+{added} spins, multiplier ×{result.State.Multiplier}", "retrigger");
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

    private async Task StopReelsAsync(Cell[][] target)
    {
        grid = (Cell[][])grid.Clone(); // keep the caller's previous grid intact
        for (var r = 0; r < Symbols.Reels; r++)
        {
            grid[r] = target[r];
            reelSpinning[r] = false;
            StateHasChanged();
            if (!reducedMotion && r < Symbols.Reels - 1) await Task.Delay(StopGapMs);
        }
        if (!reducedMotion) await Task.Delay(LandMs);
    }

    private async Task FailSpinAsync(string error, bool refresh)
    {
        Array.Fill(reelSpinning, false);
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
            if (overlay is not null && overlayTimer is null) StartOverlayTimer();
            if (!busy && overlay is null && nextSpin is null) await SpinAsync();
        }
        else if (!InFeature)
        {
            CancelNextSpin();
        }
    }

    private void StopAuto()
    {
        autoOn = false;
        CancelOverlayTimer();
    }

    // ---------- overlays ----------

    private Task ShowOverlayAsync(string title, string text, string kind)
    {
        overlayDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay = new OverlayModel(title, text, kind);
        if (autoOn) StartOverlayTimer();
        StateHasChanged();
        return overlayDone.Task;
    }

    private void DismissOverlay()
    {
        if (overlay is null) return;
        overlay = null;
        CancelOverlayTimer();
        var done = overlayDone;
        overlayDone = null;
        StateHasChanged();
        done?.TrySetResult();
    }

    private void StartOverlayTimer()
    {
        CancelOverlayTimer();
        overlayTimer = new CancellationTokenSource();
        _ = AutoDismissAsync(overlayTimer.Token);
    }

    private async Task AutoDismissAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(OverlayAutoMs, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await InvokeAsync(DismissOverlay);
    }

    private void CancelOverlayTimer()
    {
        overlayTimer?.Cancel();
        overlayTimer?.Dispose();
        overlayTimer = null;
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
