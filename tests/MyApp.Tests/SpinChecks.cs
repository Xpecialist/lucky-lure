namespace MyApp.Tests;

/// <summary>
/// Rule checks that must hold for EVERY spin, whatever the random grid ("invariants").
/// Each test feeds thousands of real spins through these. Assert messages carry the
/// offending values so a failure is easy to diagnose.
/// </summary>
internal static class SpinChecks
{
    private const int MaxRespins = 2;

    public static void Grid(SpinResult r)
    {
        GridShape(r.Grid, r.WasFreeSpin, r.Bet);
        foreach (var step in r.Respins)
            GridShape(step.Grid, wasFree: false, r.Bet);

        var cells = Play.Cells(r).ToList();
        Assert.Equal(FinalGrid(r).SelectMany(reel => reel).Count(c => c.Symbol == Symbol.Scatter), r.ScatterCount); // final grid
        Assert.Equal(cells.Count(c => c.Symbol == Symbol.Wild), r.WildCount);

        Respins(r);
    }

    /// <summary>The nudge respin rule, step by step (see API_CONTRACT.md "Nudge respin").</summary>
    public static void Respins(SpinResult r)
    {
        Assert.NotNull(r.Respins); // always an array, [] when none
        if (r.WasFreeSpin)
        {
            Assert.Empty(r.Respins);
            return;
        }
        Assert.True(r.Respins.Count <= MaxRespins, $"{r.Respins.Count} respins (cap {MaxRespins}).");

        var previous = r.Grid;
        foreach (var step in r.Respins)
        {
            var qualifying = NudgeReels(previous);
            Assert.True(qualifying is not null, "Respin happened although the previous grid doesn't qualify.");
            Assert.Equal(qualifying, step.HeldReels);

            foreach (var reel in step.HeldReels)
            {
                // Held reel: shifted down one row (Fish keep their value), new non-Scatter on top.
                Assert.Equal(previous[reel][0], step.Grid[reel][1]);
                Assert.Equal(previous[reel][1], step.Grid[reel][2]);
                Assert.NotEqual(Symbol.Scatter, step.Grid[reel][0].Symbol);
            }
            previous = step.Grid; // non-held reels: anything legal (checked by GridShape)
        }

        // The sequence stops as soon as the condition fails (or at the cap).
        if (r.Respins.Count < MaxRespins)
            Assert.Null(NudgeReels(previous));
    }

    public static void Money(SpinResult r)
    {
        var allLineWins = AllLineWins(r).ToList();
        decimal[] amounts =
        [
            r.Bet, r.ScatterWin, r.CollectWin, r.TotalWin, r.State.Balance, r.State.FreeSpinWin, r.State.FreeSpinBet,
            .. allLineWins.Select(w => w.Win),
            .. AllGrids(r).SelectMany(g => g.SelectMany(reel => reel)).Where(c => c.Value is not null).Select(c => c.Value!.Value),
        ];
        Assert.All(amounts, a => Assert.True(Play.HasAtMostTwoDecimals(a), $"{a} has more than 2 decimals."));
        Assert.True(r.State.Balance >= 0, $"Negative balance {r.State.Balance}.");
        // Line wins of the original grid and every respin, plus scatter (final grid) and collect.
        Assert.Equal(allLineWins.Sum(w => w.Win) + r.ScatterWin + r.CollectWin, r.TotalWin);
    }

    /// <summary>Checks how the state moved from <paramref name="before"/> to the spin's result.</summary>
    public static void Transition(GameState before, decimal requestedBet, SpinResult r)
    {
        var after = r.State;
        var wasFree = before.FreeSpinsRemaining > 0;
        Assert.Equal(wasFree, r.WasFreeSpin);
        Assert.True(r.TotalWin >= 0 && r.ScatterWin >= 0 && r.CollectWin >= 0, "Negative win.");

        if (!wasFree)
        {
            Assert.Equal(requestedBet, r.Bet);
            Assert.Equal(before.Balance - r.Bet + r.TotalWin, after.Balance);
            Assert.Equal(0, r.WildCount);
            Assert.Equal(0m, r.CollectWin);
            Assert.False(r.Retriggered);
            Assert.False(r.FeatureEnded);

            // Scatter fields come from the final grid (last respin, or the original).
            if (Contract.Scatter.TryGetValue(r.ScatterCount, out var award))
            {
                Assert.Equal(award.FreeSpins, r.FreeSpinsAwarded);
                Assert.Equal(Math.Round(award.Pay * r.Bet, 2), r.ScatterWin);
                // A fresh feature: bet locked, multiplier back to 1.
                Assert.Equal(new GameState(after.Balance, award.FreeSpins, award.FreeSpins, 1, 0, 0, 0m, r.Bet), after);
            }
            else
            {
                Assert.Equal(0, r.FreeSpinsAwarded);
                Assert.Equal(0m, r.ScatterWin);
                // A paid spin always clears the previous feature summary.
                Assert.Equal(new GameState(after.Balance, 0, 0, 1, 0, 0, 0m, 0m), after);
            }
            return;
        }

        // Free spin.
        Assert.Empty(r.Respins);
        Assert.Equal(before.FreeSpinBet, r.Bet);                 // bet locked, request ignored
        Assert.Equal(before.Balance + r.TotalWin, after.Balance); // nothing deducted
        Assert.Equal(0, r.ScatterCount);
        Assert.Equal(0m, r.ScatterWin);
        Assert.Equal(0, r.FreeSpinsAwarded);
        Assert.Equal(before.FreeSpinBet, after.FreeSpinBet);

        var fishSum = Play.Cells(r).Where(c => c.Symbol == Symbol.Fish).Sum(c => c.Value!.Value);
        var expectedCollect = r.WildCount == 0 ? 0m : Math.Round(fishSum * r.WildCount * before.Multiplier, 2);
        Assert.Equal(expectedCollect, r.CollectWin);

        Assert.Equal(before.WildsCollected + r.WildCount, after.WildsCollected);
        var expectedRetriggers = Math.Min(Contract.Multipliers.Length - 1, after.WildsCollected / Contract.WildsPerRetrigger);
        var newRetriggers = expectedRetriggers - before.Retriggers;
        Assert.Equal(expectedRetriggers, after.Retriggers);
        Assert.Equal(Contract.Multipliers[after.Retriggers], after.Multiplier);
        Assert.Equal(newRetriggers > 0, r.Retriggered);
        Assert.Equal(before.FreeSpinsRemaining - 1 + Contract.RetriggerSpins * newRetriggers, after.FreeSpinsRemaining);
        Assert.Equal(before.FreeSpinsTotal + Contract.RetriggerSpins * newRetriggers, after.FreeSpinsTotal);
        Assert.Equal(before.FreeSpinWin + r.TotalWin, after.FreeSpinWin);
        Assert.Equal(after.FreeSpinsRemaining == 0, r.FeatureEnded);
    }

    /// <summary>Each line win (original grid and every respin grid) follows its payline from reel 1 and matches its grid.</summary>
    public static void LineWins(SpinResult r, int[][] paylines)
    {
        LineWins(r.Grid, r.LineWins, paylines);
        foreach (var step in r.Respins)
            LineWins(step.Grid, step.LineWins, paylines);
    }

    private static void LineWins(Cell[][] grid, IReadOnlyList<LineWin> lineWins, int[][] paylines)
    {
        Assert.Equal(lineWins.Count, lineWins.Select(w => w.Line).Distinct().Count()); // ≤ 1 win per line

        foreach (var w in lineWins)
        {
            Assert.InRange(w.Line, 0, Contract.Lines - 1);
            Assert.InRange(w.Count, 3, Contract.Reels);
            Assert.NotEqual(Symbol.Scatter, w.Symbol);
            Assert.Equal(w.Count, w.Positions.Length);

            var line = paylines[w.Line];
            for (var reel = 0; reel < w.Count; reel++)
                Assert.Equal(new[] { reel, line[reel] }, w.Positions[reel]);

            var run = w.Positions.Select(p => grid[p[0]][p[1]].Symbol).ToList();
            if (w.Symbol is Symbol.Fish or Symbol.Wild)
                Assert.All(run, s => Assert.Equal(w.Symbol, s));      // Wild never substitutes for Fish
            else
                Assert.All(run, s => Assert.True(s == w.Symbol || s == Symbol.Wild,
                    $"Line {w.Line}: {s} in a {w.Symbol} run."));

            // The run is as long as it can be: the next cell on the line doesn't extend it.
            if (w.Count < Contract.Reels)
            {
                var next = grid[w.Count][line[w.Count]].Symbol;
                var extends = w.Symbol is Symbol.Fish or Symbol.Wild ? next == w.Symbol : next == w.Symbol || next == Symbol.Wild;
                Assert.False(extends, $"Line {w.Line}: {w.Symbol}×{w.Count} could be extended by {next}.");
            }
        }

        // Weak completeness check: a plain 3-of-a-kind on reels 1-3 of a line must pay.
        for (var i = 0; i < paylines.Length; i++)
        {
            var first3 = Enumerable.Range(0, 3).Select(reel => grid[reel][paylines[i][reel]].Symbol).ToList();
            if (first3.Distinct().Count() == 1 && first3[0] != Symbol.Scatter)
                Assert.Contains(lineWins, w => w.Line == i);
        }
    }

    /// <summary>5 × 3, legal symbols for the mode, ≤ 1 Scatter per reel, Fish values from the contract list.</summary>
    private static void GridShape(Cell[][] grid, bool wasFree, decimal bet)
    {
        Assert.Equal(Contract.Reels, grid.Length);
        Assert.All(grid, reel => Assert.Equal(Contract.Rows, reel.Length));

        var cells = grid.SelectMany(reel => reel).ToList();
        if (wasFree)
            Assert.DoesNotContain(cells, c => c.Symbol == Symbol.Scatter);
        else
            Assert.DoesNotContain(cells, c => c.Symbol == Symbol.Wild);

        Assert.All(grid, reel => Assert.True(reel.Count(c => c.Symbol == Symbol.Scatter) <= 1,
            "More than one Scatter on a reel."));

        var allowedFishValues = Contract.FishMultipliers.Select(m => Math.Round(m * bet, 2)).ToHashSet();
        foreach (var cell in cells)
        {
            if (cell.Symbol == Symbol.Fish)
                Assert.True(cell.Value is { } v && allowedFishValues.Contains(v),
                    $"Fish value {cell.Value} is not one of {string.Join(", ", allowedFishValues)}.");
            else
                Assert.Null(cell.Value);
        }
    }

    /// <summary>The contract's nudge condition: exactly 2 Scatters, each in row 0 or 1 → their reels; else null.</summary>
    private static int[]? NudgeReels(Cell[][] grid)
    {
        var scatters = Enumerable.Range(0, Contract.Reels)
            .SelectMany(reel => Enumerable.Range(0, Contract.Rows).Select(row => (reel, row)))
            .Where(p => grid[p.reel][p.row].Symbol == Symbol.Scatter)
            .ToList();
        return scatters.Count == 2 && scatters.All(p => p.row <= 1) ? [.. scatters.Select(p => p.reel)] : null;
    }

    private static Cell[][] FinalGrid(SpinResult r) => r.Respins.Count > 0 ? r.Respins[^1].Grid : r.Grid;

    private static IEnumerable<Cell[][]> AllGrids(SpinResult r) => [r.Grid, .. r.Respins.Select(s => s.Grid)];

    private static IEnumerable<LineWin> AllLineWins(SpinResult r) => [.. r.LineWins, .. r.Respins.SelectMany(s => s.LineWins)];
}
