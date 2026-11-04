using Xunit.Sdk;

namespace MyApp.Tests;

/// <summary>
/// Test double for <see cref="IRandomSource"/>: answers every Next(n) with a function of n.
/// Fails loudly if the answer is out of range, so a wrong fake can't silently produce nonsense.
/// </summary>
internal sealed class FuncRandom(Func<int, int> next) : IRandomSource
{
    public int Next(int maxExclusive) => Checked(next(maxExclusive), maxExclusive);

    internal static int Checked(int value, int maxExclusive) =>
        value >= 0 && value < maxExclusive
            ? value
            : throw new XunitException($"Fake RNG answered {value} for Next({maxExclusive}).");
}

/// <summary>
/// Test double for <see cref="IRandomSource"/>: replays a script of answers in order.
/// White-box: the script follows the engine's draw order (see SlotEngine.GenerateReel / Nudge / RandomNet).
/// If the engine draws more (or fewer, see <see cref="Remaining"/>) numbers than scripted, the test fails.
/// </summary>
internal sealed class ScriptedRandom(IEnumerable<int> script) : IRandomSource
{
    private readonly Queue<int> _script = new(script);

    public int Remaining => _script.Count;

    public int Next(int maxExclusive) =>
        _script.TryDequeue(out var value)
            ? FuncRandom.Checked(value, maxExclusive)
            : throw new XunitException($"The engine drew Next({maxExclusive}) but the RNG script is used up.");
}

/// <summary>
/// The RNG answers that make the engine draw a given symbol. Learned by probing the real engine (a fake RNG that
/// always answers v), so the tests don't copy the engine's private weight tables.
/// </summary>
internal static class SymbolRolls
{
    /// <summary>Scatter chance roll (out of 1000) that lands no Scatter / a Scatter on a base-game reel.</summary>
    public const int NoScatter = 999;
    public const int Scatter = 0;

    public static readonly IReadOnlyDictionary<Symbol, int> Base = Probe(free: false);
    public static readonly IReadOnlyDictionary<Symbol, int> Free = Probe(free: true);

    private static Dictionary<Symbol, int> Probe(bool free)
    {
        var rolls = new Dictionary<Symbol, int>();
        for (var v = 0; v < 1000; v++)
        {
            // Next(1000) is the scatter chance / fish value / net chance: answer "no" / last value / "no".
            // Any other Next(n) is a symbol pick: answer v (wrapped), so every cell becomes the same symbol.
            var engine = new SlotEngine(new FuncRandom(n => n == 1000 ? 999 : v % n));
            var session = free ? Play.InFeature(freeSpins: 1) : Play.RichSession();
            rolls.TryAdd(engine.Spin(session, 1m).Grid[0][0].Symbol, v);
        }
        return rolls;
    }
}
