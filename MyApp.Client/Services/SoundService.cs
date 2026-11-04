using Microsoft.JSInterop;

namespace MyApp.Client.Services;

/// <summary>Every game sound (synthesized in wwwroot/js/sound.js). The JS name is the camelCase enum name.</summary>
public enum Sfx
{
    Click, SpinStart, ReelStop, Boat, Boats3, Nudge, LineWin, BigWin, MegaWin, RollTicks,
    FishLand, NetCast, Pluck, Kaching, Twinkle, Slot, LevelUp, StingerFree, StingerRetrigger, StingerEnd,
}

/// <summary>Sounds that keep playing until stopped.</summary>
public enum SfxLoop { Whirr, Tease }

/// <summary>
/// Typed C# facade over the Web Audio module (wwwroot/js/sound.js). Registered once in DI and injected where
/// sounds are needed. Calls use synchronous in-process interop (Blazor WebAssembly only), so playing a sound is
/// fire-and-forget and never changes the game's timing. Until the module has loaded (or if audio is unavailable)
/// every call is a silent no-op. The browser only starts audio after the player's first click / key press.
/// </summary>
public sealed class SoundService(IJSRuntime js) : IAsyncDisposable
{
    private IJSInProcessObjectReference? module;
    private Task? loading;

    /// <summary>Raised after mute / volume / music change (so every control shows the same state).</summary>
    public event Action? Changed;

    public bool Muted { get; private set; }
    public double Volume { get; private set; } = 0.6;
    public bool MusicEnabled { get; private set; } = true;

    /// <summary>Loads the module once (safe to call from several components) and reads the saved settings.</summary>
    public Task InitAsync() => loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            module = await js.InvokeAsync<IJSInProcessObjectReference>("import", "./js/sound.js");
            module.InvokeVoid("init");
            Muted = module.Invoke<bool>("getMuted");
            Volume = module.Invoke<double>("getVolume");
            MusicEnabled = module.Invoke<bool>("getMusic");
            Changed?.Invoke();
        }
        catch (JSException)
        {
            module = null; // no sound, the game still works
        }
    }

    public void Play(Sfx sound, double arg = 0) => Call("play", JsName(sound.ToString()), arg);
    public void StartLoop(SfxLoop loop) => Call("startLoop", JsName(loop.ToString()));
    public void StopLoop(SfxLoop loop) => Call("stopLoop", JsName(loop.ToString()));

    /// <summary>Fades out every sound effect still playing (used when the player skips an animation).</summary>
    public void Cut() => Call("cut");

    /// <summary>Free-spin music wanted on / off (fades; also respects the music toggle and mute).</summary>
    public void Music(bool on) => Call("music", on);

    public void SetMuted(bool muted) { Muted = muted; Call("setMuted", muted); Changed?.Invoke(); }
    public void SetVolume(double volume) { Volume = Math.Clamp(volume, 0, 1); Call("setVolume", Volume); Changed?.Invoke(); }
    public void SetMusicEnabled(bool on) { MusicEnabled = on; Call("setMusic", on); Changed?.Invoke(); }

    private void Call(string fn, params object?[] args)
    {
        if (module is null) return;
        try { module.InvokeVoid(fn, args); }
        catch (JSException) { /* a sound must never break the game */ }
    }

    private static string JsName(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try
        {
            module.InvokeVoid("dispose");
            await module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            // Page is going away.
        }
        module = null;
    }
}
