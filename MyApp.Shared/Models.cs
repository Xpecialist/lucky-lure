using System.Text.Json.Serialization;

namespace MyApp.Shared;

// Serialized as names ("Fish") on both server and client.
[JsonConverter(typeof(JsonStringEnumConverter<Symbol>))]
public enum Symbol
{
    Ten, Jack, Queen, King, Ace, TackleBox, Dragonfly, Rod, Float, Fish, Wild, Scatter
}

/// <summary>One grid position. Value is set only for Fish (cash amount).</summary>
public sealed record Cell(Symbol Symbol, decimal? Value);

/// <summary>Positions are [reel, row] pairs.</summary>
public sealed record LineWin(int Line, Symbol Symbol, int Count, decimal Win, int[][] Positions);

public sealed record ScatterAward(decimal Pay, int FreeSpins);

public sealed record GameState(
    decimal Balance,
    int FreeSpinsRemaining,
    int FreeSpinsTotal,
    int Multiplier,
    int WildsCollected,
    int Retriggers,
    decimal FreeSpinWin,
    decimal FreeSpinBet);

public sealed record SpinResult(
    Cell[][] Grid,
    decimal Bet,
    bool WasFreeSpin,
    IReadOnlyList<LineWin> LineWins,
    int ScatterCount,
    decimal ScatterWin,
    int WildCount,
    decimal CollectWin,
    decimal TotalWin,
    int FreeSpinsAwarded,
    bool Retriggered,
    bool FeatureEnded,
    IReadOnlyList<RespinStep> Respins,
    NetCast? Net,
    GameState State);

[JsonConverter(typeof(JsonStringEnumConverter<NetSize>))]
public enum NetSize { Small, Big }

/// <summary>Free-spin net cast: non-Wild cells turned into Fish. <see cref="Grid"/> is the grid after the transform.</summary>
public sealed record NetCast(NetSize Size, int[][] Positions, Cell[][] Grid);

/// <summary>One nudge respin: the 2 scatter reels held and shifted down a row, the other reels respun.</summary>
public sealed record RespinStep(Cell[][] Grid, int[] HeldReels, IReadOnlyList<LineWin> LineWins);

public sealed record GameInfo(
    IReadOnlyList<decimal> Bets,
    int[][] Paylines,
    IReadOnlyDictionary<string, decimal[]> Paytable,
    IReadOnlyDictionary<string, ScatterAward> Scatter,
    IReadOnlyList<int> Multipliers,
    int WildsPerRetrigger);

public sealed record SpinRequest(decimal? Bet);
