using MyApp.Shared;

namespace MyApp.Game;

/// <summary>
/// A decided net cast before it is applied: size, the distinct non-Wild cells ([reel, row]) and each new Fish's
/// value multiplier (× bet), in the same order. Produced by the engine's RNG, or supplied by tests via Apply.
/// </summary>
internal sealed record NetPlan(NetSize Size, int[][] Positions, int[] Values);
