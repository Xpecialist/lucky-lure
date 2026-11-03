using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MyApp.Game;

public sealed class SessionStore
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(24);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
    private long _lastPruneTicks = DateTime.UtcNow.Ticks;

    public GameSession? Get(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_sessions.TryGetValue(id, out var session))
            return null;

        session.LastSeenUtc = DateTime.UtcNow;
        return session;
    }

    public GameSession Create()
    {
        PruneIfDue();
        while (true)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var session = new GameSession(id);
            if (_sessions.TryAdd(id, session))
                return session;
        }
    }

    private void PruneIfDue()
    {
        var now = DateTime.UtcNow;
        var last = Interlocked.Read(ref _lastPruneTicks);
        if (now.Ticks - last < PruneInterval.Ticks)
            return;
        // Only one caller wins the right to prune this round.
        if (Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) != last)
            return;

        var cutoff = now - IdleLimit;
        foreach (var (id, session) in _sessions)
        {
            if (session.LastSeenUtc < cutoff)
                _sessions.TryRemove(id, out _);
        }
    }
}
