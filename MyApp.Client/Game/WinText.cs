using MyApp.Shared;

namespace MyApp.Client.Game;

/// <summary>Builds the message line and win highlights for a spin result.</summary>
public static class WinText
{
    public static string Describe(SpinResult res)
    {
        var parts = new List<string>();
        var lineWins = res.LineWins ?? [];
        foreach (var w in lineWins.Take(3))
            parts.Add($"{Symbols.Name(w.Symbol)} ×{w.Count} on line {w.Line + 1} — {Fmt.Money(w.Win)}");
        if (lineWins.Count > 3) parts.Add($"+{lineWins.Count - 3} more line wins");
        if (res.ScatterWin > 0) parts.Add($"{res.ScatterCount} Boats pay {Fmt.Money(res.ScatterWin)}");
        if (res.CollectWin > 0)
        {
            parts.Add(res.WildCount > 1
                ? $"{res.WildCount} Fishermen reeled in {Fmt.Money(res.CollectWin)}!"
                : $"Fisherman reeled in {Fmt.Money(res.CollectWin)}!");
        }

        if (parts.Count == 0)
        {
            if (res.WildCount > 0) return "A Fisherman landed — but no fish to reel in.";
            return res.WasFreeSpin ? "No catch this spin." : "No win — cast again!";
        }
        if (parts.Count > 1 && res.TotalWin > 0) parts.Add($"Total {Fmt.Money(res.TotalWin)}");
        return string.Join(" · ", parts);
    }

    /// <summary>Winning positions as marks[reel][row]; null when nothing won.</summary>
    public static bool[][]? Marks(SpinResult res)
    {
        var marks = new bool[Symbols.Reels][];
        for (var r = 0; r < Symbols.Reels; r++) marks[r] = new bool[Symbols.Rows];
        var any = false;

        void Mark(int reel, int row)
        {
            if (reel is < 0 or >= Symbols.Reels || row is < 0 or >= Symbols.Rows) return;
            marks[reel][row] = true;
            any = true;
        }

        foreach (var w in res.LineWins ?? [])
            foreach (var p in w.Positions ?? [])
                if (p is { Length: >= 2 }) Mark(p[0], p[1]);

        for (var r = 0; r < res.Grid.Length; r++)
        {
            for (var row = 0; row < res.Grid[r].Length; row++)
            {
                var s = res.Grid[r][row].Symbol;
                if (res.ScatterWin > 0 && s == Symbol.Scatter) Mark(r, row);
                if (res.CollectWin > 0 && s is Symbol.Wild or Symbol.Fish) Mark(r, row);
            }
        }
        return any ? marks : null;
    }
}
