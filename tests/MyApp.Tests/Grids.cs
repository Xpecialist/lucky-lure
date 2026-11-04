namespace MyApp.Tests;

/// <summary>
/// Hand-built grids for the SlotEngine.Apply seam, written the way they look on screen:
/// three rows (top, middle, bottom), five codes each, e.g. <c>Grids.Parse(1m, "J Q J Q J", "W A A A R", "K A K A K")</c>.
/// </summary>
/// <remarks>
/// Codes: 10 J Q K A TB (TackleBox) DF (Dragonfly) R (Rod) FL (Float) W (Wild) S (Scatter),
/// F&lt;n&gt; = Fish worth n × bet (e.g. F5). The result is grid[reel][row], like SpinResult.Grid.
/// </remarks>
internal static class Grids
{
    // The "blank" rows: reel 1 only holds {J, R, K} and reel 2 only {Q, FL, A}. Two neighbouring reels never share
    // a symbol, so no payline can start a run. Tests swap single cells to build exactly the case they need.
    public const string BlankTop = "J Q J Q J";
    public const string BlankMiddle = "R FL R FL R";
    public const string BlankBottom = "K A K A K";

    public static Cell[][] Blank(decimal bet = 1m) => Parse(bet, BlankTop, BlankMiddle, BlankBottom);

    public static Cell[][] Parse(decimal bet, string top, string middle, string bottom)
    {
        string[][] rows = [Split(top), Split(middle), Split(bottom)];
        return [.. Enumerable.Range(0, Contract.Reels).Select(reel => rows.Select(row => Cell(row[reel], bet)).ToArray())];
    }

    public static Cell Cell(string code, decimal bet) => code switch
    {
        "10" => new(Symbol.Ten, null),
        "J" => new(Symbol.Jack, null),
        "Q" => new(Symbol.Queen, null),
        "K" => new(Symbol.King, null),
        "A" => new(Symbol.Ace, null),
        "TB" => new(Symbol.TackleBox, null),
        "DF" => new(Symbol.Dragonfly, null),
        "R" => new(Symbol.Rod, null),
        "FL" => new(Symbol.Float, null),
        "W" => new(Symbol.Wild, null),
        "S" => new(Symbol.Scatter, null),
        ['F', .. var n] => new(Symbol.Fish, int.Parse(n) * bet),
        _ => throw new ArgumentException($"Unknown cell code '{code}'.", nameof(code)),
    };

    /// <summary>A copy of <paramref name="grid"/> with some cells replaced (positions are [reel, row]).</summary>
    public static Cell[][] With(Cell[][] grid, Cell cell, params (int Reel, int Row)[] positions)
    {
        var copy = grid.Select(reel => (Cell[])reel.Clone()).ToArray();
        foreach (var (reel, row) in positions)
            copy[reel][row] = cell;
        return copy;
    }

    /// <summary>
    /// Parses "reel,row" pairs separated by spaces, e.g. "0,0 3,1" → [(0, 0), (3, 1)].
    /// Used to keep [InlineData] rows readable.
    /// </summary>
    public static (int Reel, int Row)[] Positions(string pairs) =>
    [
        .. pairs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(','))
            .Select(p => (int.Parse(p[0]), int.Parse(p[1]))),
    ];

    private static string[] Split(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == Contract.Reels
            ? parts
            : throw new ArgumentException($"A row needs {Contract.Reels} codes: '{s}'.", nameof(s));
    }
}
