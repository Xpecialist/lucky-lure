namespace MyApp.Client.Game;

/// <summary>
/// An awaitable pause the player can end early (click / Space / Enter). Used for overlays (no timeout:
/// they wait for the player) and win celebrations (timeout, skippable).
/// </summary>
public sealed class SkipGate
{
    private TaskCompletionSource? pending;

    /// <summary>True while someone is waiting on the gate.</summary>
    public bool IsWaiting => pending is not null;

    /// <summary>Waits until <see cref="Skip"/> is called or, when given, the timeout elapses.</summary>
    public async Task WaitAsync(int? timeoutMs = null)
    {
        pending?.TrySetResult();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = tcs;
        try
        {
            if (timeoutMs is int ms) await Task.WhenAny(tcs.Task, Task.Delay(ms));
            else await tcs.Task;
        }
        finally
        {
            if (ReferenceEquals(pending, tcs)) pending = null;
        }
    }

    /// <summary>Ends the current wait (no-op when nobody is waiting).</summary>
    public void Skip() => pending?.TrySetResult();
}
