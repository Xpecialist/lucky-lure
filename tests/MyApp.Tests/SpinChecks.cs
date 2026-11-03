namespace MyApp.Tests;

/// <summary>
/// Rule checks that must hold for EVERY spin, whatever the random grid ("invariants").
/// Each test feeds thousands of real spins through these. Assert messages carry the
/// offending values so a failure is easy to diagnose.
/// </summary>
internal static class SpinChecks
{
    public static void Grid(SpinResult r)
    {
        Assert.Equal(Contract.Reels, r.Grid.Length);
        Assert.All(r.Grid, reel => Assert.Equal(Contract.Rows, reel.Length));

        var cells = Play.Cells(r).ToList();
        if (r.WasFreeSpin)
            Assert.DoesNotContain(cells, c => c.Symbol == Symbol.Scatter);
        else
            Assert.DoesNotContain(cells, c => c.Symbol == Symbol.Wild);

        Assert.All(r.Grid, reel => Assert.True(reel.Count(c => c.Symbol == Symbol.Scatter) <= 1,
            "More than one Scatter on a reel."));

        var allowedFishValues = Contract.FishMultipliers.Select(m => Math.Round(m * r.Bet, 2)).ToHashSet();
        foreach (var cell in cells)
        {
            if (cell.Symbol == Symbol.Fish)
                Assert.True(cell.Value is { } v && allowedFishValues.Contains(v),
                    $"Fish value {cell.Value} is not one of {string.Join(", ", allowedFishValues)}.");
            else
                Assert.Null(cell.Value);
        }

        Assert.Equal(cells.Count(c => c.Symbol == Symbol.Scatter), r.ScatterCount);
        Assert.Equal(cells.Count(c => c.Symbol == Symbol.Wild), r.WildCount);
    }

    public static void Money(SpinResult r)
    {
        decimal[] amounts =
        [
            r.Bet, r.ScatterWin, r.CollectWin, r.TotalWin, r.State.Balance, r.State.FreeSpinWin, r.State.FreeSpinBet,
            .. r.LineWins.Select(w => w.Win),
            .. Play.Cells(r).Where(c => c.Value is not null).Select(c => c.Value!.Value),
        ];
        Assert.All(amounts, a => Assert.True(Play.HasAtMostTwoDecimals(a), $"{a} has more than 2 decimals."));
        Assert.True(r.State.Balance >= 0, $"Negative balance {r.State.Balance}.");
        Assert.Equal(r.LineWins.Sum(w => w.Win) + r.ScatterWin + r.CollectWin, r.TotalWin);
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

    /// <summary>Each line win follows its payline from reel 1 and matches the grid.</summary>
    public static void LineWins(SpinResult r, int[][] paylines)
    {
        Assert.Equal(r.LineWins.Count, r.LineWins.Select(w => w.Line).Distinct().Count()); // ≤ 1 win per line

        foreach (var w in r.LineWins)
        {
            Assert.InRange(w.Line, 0, Contract.Lines - 1);
            Assert.InRange(w.Count, 3, Contract.Reels);
            Assert.NotEqual(Symbol.Scatter, w.Symbol);
            Assert.Equal(w.Count, w.Positions.Length);

            var line = paylines[w.Line];
            for (var reel = 0; reel < w.Count; reel++)
                Assert.Equal(new[] { reel, line[reel] }, w.Positions[reel]);

            var run = w.Positions.Select(p => r.Grid[p[0]][p[1]].Symbol).ToList();
            if (w.Symbol is Symbol.Fish or Symbol.Wild)
                Assert.All(run, s => Assert.Equal(w.Symbol, s));      // Wild never substitutes for Fish
            else
                Assert.All(run, s => Assert.True(s == w.Symbol || s == Symbol.Wild,
                    $"Line {w.Line}: {s} in a {w.Symbol} run."));

            // The run is as long as it can be: the next cell on the line doesn't extend it.
            if (w.Count < Contract.Reels)
            {
                var next = r.Grid[w.Count][line[w.Count]].Symbol;
                var extends = w.Symbol is Symbol.Fish or Symbol.Wild ? next == w.Symbol : next == w.Symbol || next == Symbol.Wild;
                Assert.False(extends, $"Line {w.Line}: {w.Symbol}×{w.Count} could be extended by {next}.");
            }
        }

        // Weak completeness check: a plain 3-of-a-kind on reels 1-3 of a line must pay.
        for (var i = 0; i < paylines.Length; i++)
        {
            var first3 = Enumerable.Range(0, 3).Select(reel => r.Grid[reel][paylines[i][reel]].Symbol).ToList();
            if (first3.Distinct().Count() == 1 && first3[0] != Symbol.Scatter)
                Assert.Contains(r.LineWins, w => w.Line == i);
        }
    }
}
