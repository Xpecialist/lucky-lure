using Xunit.Sdk;

namespace MyApp.Tests;

/// <summary>
/// Game rules copied from MyApp/API_CONTRACT.md. Tests compare the engine against these
/// (the contract), not against the engine's own private tables.
/// </summary>
internal static class Contract
{
    public const int Reels = 5;
    public const int Rows = 3;
    public const int Lines = 10;
    public const int WildsPerRetrigger = 4;
    public const int RetriggerSpins = 10;
    public const decimal StartingBalance = 1000m;

    public static readonly decimal[] Bets = [0.10m, 0.20m, 0.50m, 1m, 2m, 5m, 10m];
    public static readonly int[] Multipliers = [1, 2, 3, 10];
    public static readonly int[] FishMultipliers = [2, 5, 10, 15, 20, 25, 50, 100];

    /// <summary>Scatter count -> (pay × total bet, free spins awarded).</summary>
    public static readonly IReadOnlyDictionary<int, (decimal Pay, int FreeSpins)> Scatter =
        new Dictionary<int, (decimal, int)>
        {
            [3] = (2m, 10),
            [4] = (10m, 15),
            [5] = (50m, 20),
        };

    /// <summary>xUnit [MemberData] source: one row per valid bet.</summary>
    public static TheoryData<decimal> AllBets()
    {
        var data = new TheoryData<decimal>();
        foreach (var bet in Bets)
            data.Add(bet);
        return data;
    }
}

/// <summary>Helpers that drive the real engine through its public API.</summary>
internal static class Play
{
    /// <summary>A session with enough credits that paid spins never fail for lack of balance.</summary>
    public static GameSession RichSession(decimal balance = 10_000_000m) => new("test") { Balance = balance };

    /// <summary>A session placed directly inside a free-spin feature (arranged through public setters).</summary>
    public static GameSession InFeature(int freeSpins, decimal freeSpinBet = 1m) => new("test")
    {
        Balance = 1000m,
        FreeSpinsRemaining = freeSpins,
        FreeSpinsTotal = freeSpins,
        FreeSpinBet = freeSpinBet,
    };

    /// <summary>Spins <paramref name="count"/> times, yielding the state before each spin with its result.</summary>
    public static IEnumerable<(GameState Before, SpinResult Result)> Spins(
        SlotEngine engine, GameSession session, decimal bet, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var before = engine.Snapshot(session);
            yield return (before, engine.Spin(session, bet));
        }
    }

    /// <summary>
    /// Spins until <paramref name="condition"/> holds and returns that spin.
    /// Fails the test after <paramref name="maxSpins"/> so a broken engine can't hang the run.
    /// </summary>
    public static SpinResult Until(
        SlotEngine engine, GameSession session, decimal bet, Func<SpinResult, bool> condition, int maxSpins = 100_000)
    {
        for (var i = 0; i < maxSpins; i++)
        {
            var result = engine.Spin(session, bet);
            if (condition(result))
                return result;
        }
        throw new XunitException($"Condition not reached within {maxSpins} spins.");
    }

    public static IEnumerable<Cell> Cells(SpinResult r) => r.Grid.SelectMany(reel => reel);

    public static bool HasAtMostTwoDecimals(decimal value) => value == Math.Round(value, 2);
}
