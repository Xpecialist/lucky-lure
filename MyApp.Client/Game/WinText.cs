using MyApp.Shared;

namespace MyApp.Client.Game;

/// <summary>
/// Builds the message line and win highlights for one stage of a spin: the original grid or a nudge respin.
/// Scatter pay and the Fisherman collect belong to the final stage only.
/// </summary>
public static class WinText
{
    public static string Describe(SpinResult res, IReadOnlyList<LineWin> lineWins, bool final)
    {
        var parts = new List<string>();
        foreach (var w in lineWins.Take(3))
            parts.Add($"{Symbols.Name(w.Symbol)} ×{w.Count} on line {w.Line + 1} — {Fmt.Money(w.Win)}");
        if (lineWins.Count > 3) parts.Add($"+{lineWins.Count - 3} more line wins");

        if (!final) return parts.Count > 0 ? string.Join(" · ", parts) : "No line win on this grid…";

        if (res.ScatterWin > 0) parts.Add($"{res.ScatterCount} Boats pay {Fmt.Money(res.ScatterWin)}");
        if (res.CollectWin > 0)
        {
            parts.Add(res.WildCount > 1
                ? $"{res.WildCount} Fishermen reeled in {Fmt.Money(res.CollectWin)}!"
                : $"Fisherman reeled in {Fmt.Money(res.CollectWin)}!");
        }

        var hadRespins = res.Respins is { Count: > 0 };
        if (parts.Count == 0)
        {
            if (res.TotalWin > 0) return $"Total win {Fmt.Money(res.TotalWin)}"; // earlier respin stages paid
            if (res.WildCount > 0) return "A Fisherman landed — but no fish to reel in.";
            return res.WasFreeSpin ? "No catch this spin." : "No win — cast again!";
        }
        if ((parts.Count > 1 || hadRespins) && res.TotalWin > 0) parts.Add($"Total {Fmt.Money(res.TotalWin)}");
        return string.Join(" · ", parts);
    }

    /// <summary>Winning positions on <paramref name="grid"/> as marks[reel][row]; null when nothing won.</summary>
    public static bool[][]? Marks(SpinResult res, Cell[][] grid, IReadOnlyList<LineWin> lineWins, bool final)
    {
        var marks = NewMarks();
        var any = false;

        void Mark(int reel, int row)
        {
            if (reel is < 0 or >= Symbols.Reels || row is < 0 or >= Symbols.Rows) return;
            marks[reel][row] = true;
            any = true;
        }

        foreach (var w in lineWins)
            foreach (var p in w.Positions ?? [])
                if (p is { Length: >= 2 }) Mark(p[0], p[1]);

        if (final)
        {
            ForEachCell(grid, (r, row, s) =>
            {
                if (res.ScatterWin > 0 && s == Symbol.Scatter) Mark(r, row);
                if (res.CollectWin > 0 && s is Symbol.Wild or Symbol.Fish) Mark(r, row);
            });
        }
        return any ? marks : null;
    }

    /// <summary>The Scatters on the given reels (the nudge "tease" before a respin).</summary>
    public static bool[][]? ScatterMarks(Cell[][] grid, IReadOnlyCollection<int> reels)
    {
        var marks = NewMarks();
        var any = false;
        ForEachCell(grid, (r, row, s) =>
        {
            if (s != Symbol.Scatter || !reels.Contains(r)) return;
            marks[r][row] = true;
            any = true;
        });
        return any ? marks : null;
    }

    private static bool[][] NewMarks()
    {
        var marks = new bool[Symbols.Reels][];
        for (var r = 0; r < Symbols.Reels; r++) marks[r] = new bool[Symbols.Rows];
        return marks;
    }

    private static void ForEachCell(Cell[][] grid, Action<int, int, Symbol> visit)
    {
        for (var r = 0; r < grid.Length && r < Symbols.Reels; r++)
            for (var row = 0; row < grid[r].Length && row < Symbols.Rows; row++)
                visit(r, row, grid[r][row].Symbol);
    }
}
