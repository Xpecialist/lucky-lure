using MyApp.Shared;

namespace MyApp.Client.Game;

/// <summary>How a symbol looks. <see cref="Css"/> is also the id of its artwork in <see cref="SymbolArt"/>.</summary>
public sealed record SymbolMeta(string Name, string Css, string? Tag = null)
{
    /// <summary>Fragment reference for <c>&lt;use href="..."&gt;</c>.</summary>
    public string Art => "#" + Css;
}

/// <summary>Symbol metadata plus random filler cells for the spinning strips.</summary>
public static class Symbols
{
    public const int Reels = 5;
    public const int Rows = 3;

    private static readonly Dictionary<Symbol, SymbolMeta> Meta = new()
    {
        [Symbol.Ten] = new("Ten", "sym-ten"),
        [Symbol.Jack] = new("Jack", "sym-jack"),
        [Symbol.Queen] = new("Queen", "sym-queen"),
        [Symbol.King] = new("King", "sym-king"),
        [Symbol.Ace] = new("Ace", "sym-ace"),
        [Symbol.TackleBox] = new("Tackle Box", "sym-tacklebox"),
        [Symbol.Dragonfly] = new("Dragonfly", "sym-dragonfly"),
        [Symbol.Rod] = new("Rod", "sym-rod"),
        [Symbol.Float] = new("Float", "sym-float"),
        [Symbol.Fish] = new("Fish", "sym-fish"),
        [Symbol.Wild] = new("Fisherman", "sym-wild", "WILD"),
        [Symbol.Scatter] = new("Boat", "sym-scatter", "SCATTER"),
    };
    // Weighted filler pool for spinning strips and the starting grid.
    private static readonly Symbol[] Filler =
    [
        Symbol.Ten, Symbol.Ten, Symbol.Jack, Symbol.Jack, Symbol.Queen, Symbol.Queen,
        Symbol.King, Symbol.King, Symbol.Ace, Symbol.Ace,
        Symbol.TackleBox, Symbol.Dragonfly, Symbol.Rod, Symbol.Float, Symbol.Fish, Symbol.Fish,
    ];

    private static readonly int[] FishMultipliers = [2, 5, 10, 15, 20, 25, 50, 100];

    public static SymbolMeta Of(Symbol symbol) =>
        Meta.TryGetValue(symbol, out var m) ? m : new(symbol.ToString(), "sym-unknown");

    /// <summary>Looks up a paytable key ("Ten", "Fish", ...).</summary>
    public static SymbolMeta Of(string key) =>
        Enum.TryParse<Symbol>(key, ignoreCase: false, out var s) ? Of(s) : new(key, "sym-unknown");

    public static string Name(Symbol symbol) => Of(symbol).Name;

    /// <summary>A random cell. Free-spin strips may show the Fisherman; base-game strips may show Boats.</summary>
    public static Cell RandomCell(decimal bet, bool freeMode, bool allowSpecial = true)
    {
        var pool = Filler.Length + (allowSpecial ? 1 : 0);
        var i = Random.Shared.Next(pool);
        var symbol = i < Filler.Length ? Filler[i] : freeMode ? Symbol.Wild : Symbol.Scatter;
        return symbol == Symbol.Fish
            ? new Cell(symbol, Math.Round(FishMultipliers[Random.Shared.Next(FishMultipliers.Length)] * bet, 2))
            : new Cell(symbol, null);
    }

    public static Cell[] RandomCells(int count, decimal bet, bool freeMode)
    {
        var cells = new Cell[count];
        for (var i = 0; i < count; i++) cells[i] = RandomCell(bet, freeMode);
        return cells;
    }

    /// <summary>Starting grid: no Wild, at most one Boat per reel and two in total.</summary>
    public static Cell[][] RandomGrid(decimal bet)
    {
        var grid = new Cell[Reels][];
        var scatters = 0;
        for (var r = 0; r < Reels; r++)
        {
            grid[r] = new Cell[Rows];
            var reelHasScatter = false;
            for (var row = 0; row < Rows; row++)
            {
                var cell = RandomCell(bet, freeMode: false, allowSpecial: !reelHasScatter && scatters < 2);
                if (cell.Symbol == Symbol.Scatter) { reelHasScatter = true; scatters++; }
                grid[r][row] = cell;
            }
        }
        return grid;
    }
}
