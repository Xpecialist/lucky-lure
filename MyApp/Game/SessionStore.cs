using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MyApp.Game;

/// <summary>In-memory player sessions. Time comes from <see cref="TimeProvider"/> so expiry is testable.</summary>
public sealed class SessionStore(TimeProvider time)
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(24);
    // Sessions that never spun (bots, one-off visits) go much sooner.
    private static readonly TimeSpan UnplayedIdleLimit = TimeSpan.FromMinutes(30);
    // One O(n) sweep at most every 10 minutes, run by whichever Create() call is due.
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
    private long _lastPruneTicks = time.GetUtcNow().UtcTicks;

    public GameSession? Get(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_sessions.TryGetValue(id, out var session))
            return null;

        session.LastSeenUtc = Now;
        return session;
    }

    public GameSession Create()
    {
        PruneIfDue();
        while (true)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var session = new GameSession(id) { LastSeenUtc = Now };
            if (_sessions.TryAdd(id, session))
                return session;
        }
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private void PruneIfDue()
    {
        var now = Now;
        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now.Ticks - last < PruneInterval.Ticks)
            return;
        // Only one caller wins the right to prune this round.
        if (Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) != last)
            return;

        var cutoff = now - IdleLimit;
        var unplayedCutoff = now - UnplayedIdleLimit;
        foreach (var (id, session) in _sessions)
        {
            if (session.LastSeenUtc < (session.HasSpun ? cutoff : unplayedCutoff))
                _sessions.TryRemove(id, out _);
        }
    }
}
