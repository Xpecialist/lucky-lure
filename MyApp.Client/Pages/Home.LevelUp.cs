using MyApp.Client.Services;
using MyApp.Shared;

namespace MyApp.Client.Pages;

// Free-spin multiplier level-up: on a retrigger the banner keeps showing the old multiplier and spins until
// the collect has filled the fisherman bar; then the reached "+10 FREE SPINS Γ—N" plate bursts, the multiplier
// pops to its new value and "+10" floats up from the spins counter. After that the RETRIGGER overlay waits for
// the player as before. Skippable (celebrationGate); reduced motion = a plain highlight.
public partial class Home
{
    private const int LevelUpLeadMs = 450;   // the filled bar and win line settle first
    private const int LevelUpMs = 1600;

    private sealed record BannerHold(int Multiplier, int SpinsRemaining, int SpinsTotal);
    private sealed record LevelUpModel(int Level, int Added, int Key);

    private BannerHold? bannerHold;   // old banner numbers while the retrigger is not celebrated yet
    private LevelUpModel? levelUp;    // non-null while the burst plays
    private int levelUpKey;

    /// <summary>The state the banner shows: the real one, or the held pre-retrigger numbers.</summary>
    private GameState BannerState => bannerHold is { } h && state is not null
        ? state with { Multiplier = h.Multiplier, FreeSpinsRemaining = h.SpinsRemaining, FreeSpinsTotal = h.SpinsTotal }
        : state!;

    /// <summary>What the banner showed before this spin's retrigger (one spin used, nothing added yet).</summary>
    private static BannerHold BannerHoldFor(GameState prev) =>
        new(prev.Multiplier, Math.Max(0, prev.FreeSpinsRemaining - 1), prev.FreeSpinsTotal);

    private async Task PlayLevelUpAsync(GameState now, int added)
    {
        // levelUp is set first so the skip layer is up for the lead-in as well.
        levelUp = new LevelUpModel(-1, 0, levelUpKey);
        StateHasChanged();
        var skipped = await celebrationGate.WaitAsync(reducedMotion ? ReducedPauseMs : LevelUpLeadMs);

        bannerHold = null;
        levelUp = new LevelUpModel(Math.Max(0, now.Retriggers - 1), added, ++levelUpKey);
        if (!skipped) Sound.Play(Sfx.LevelUp);
        StateHasChanged();
        if (!skipped) await celebrationGate.WaitAsync(reducedMotion ? ReducedPauseMs * 2 : LevelUpMs);
        levelUp = null;
        StateHasChanged();
    }
}
