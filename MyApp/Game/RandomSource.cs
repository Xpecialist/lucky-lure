using System.Security.Cryptography;

namespace MyApp.Game;

/// <summary>Source of random numbers for the engine. Swappable so tests and tools can control it.</summary>
public interface IRandomSource
{
    /// <summary>A uniformly random integer in [0, <paramref name="maxExclusive"/>).</summary>
    int Next(int maxExclusive);
}

/// <summary>Production RNG: cryptographically secure, thread-safe.</summary>
public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}
