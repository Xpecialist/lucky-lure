using System.Security.Cryptography;
using MyApp.Shared;

namespace MyApp.Game;

public sealed class SlotEngine
{
    public const decimal StartingBalance = 1000m;

    private const int Reels = 5;
    private const int Rows = 3;
    private const int LinesCount = 10;
    private const int WildsPerRetrigger = 4;
    private const int RetriggerSpins = 10;
    // Chance per reel (out of 1000) that a Scatter lands in the base game.
    // P(3+ of 5) at 8.5% ≈ 0.54% ≈ 1 in 186 spins.
    private const int ScatterChancePerMille = 85;

    private static readonly decimal[] Bets = [0.10m, 0.20m, 0.50m, 1m, 2m, 5m, 10m];
    private static readonly int[] Multipliers = [1, 2, 3, 10];

    // Each line = row index (0-2) per reel.
    private static readonly int[][] Paylines =
    [
        [1, 1, 1, 1, 1],
        [0, 0, 0, 0, 0],
        [2, 2, 2, 2, 2],
        [0, 1, 2, 1, 0],
        [2, 1, 0, 1, 2],
        [0, 0, 1, 2, 2],
        [2, 2, 1, 0, 0],
        [1, 0, 0, 0, 1],
        [1, 2, 2, 2, 1],
        [0, 1, 1, 1, 0],
    ];

    // Pay for 3 / 4 / 5 of a kind, in line bets.
    private static readonly Dictionary<Symbol, decimal[]> Paytable = new()
    {
        [Symbol.Ten] = [2, 10, 50],
        [Symbol.Jack] = [2, 10, 50],
        [Symbol.Queen] = [3, 15, 60],
        [Symbol.King] = [3, 15, 75],
        [Symbol.Ace] = [4, 20, 100],
        [Symbol.TackleBox] = [5, 25, 125],
        [Symbol.Dragonfly] = [5, 30, 150],
        [Symbol.Rod] = [8, 40, 200],
        [Symbol.Float] = [10, 50, 250],
        [Symbol.Fish] = [5, 20, 100],
        [Symbol.Wild] = [20, 100, 500],
    };

    // Scatter count -> pay (× total bet) and free spins.
    private static readonly Dictionary<int, ScatterAward> ScatterAwards = new()
    {
        [3] = new(2, 10),
        [4] = new(10, 15),
        [5] = new(50, 20),
    };

    // Scatter is placed separately (max one per reel), so it is not in these tables.
    private static readonly (Symbol Item, int Weight)[] BaseWeights =
    [
        (Symbol.Ten, 30), (Symbol.Jack, 30), (Symbol.Queen, 26), (Symbol.King, 24), (Symbol.Ace, 22),
        (Symbol.TackleBox, 14), (Symbol.Dragonfly, 12), (Symbol.Rod, 10), (Symbol.Float, 8), (Symbol.Fish, 10),
    ];

    private static readonly (Symbol Item, int Weight)[] FreeWeights =
    [
        (Symbol.Ten, 28), (Symbol.Jack, 28), (Symbol.Queen, 24), (Symbol.King, 22), (Symbol.Ace, 20),
        (Symbol.TackleBox, 12), (Symbol.Dragonfly, 10), (Symbol.Rod, 8), (Symbol.Float, 7), (Symbol.Fish, 24),
        (Symbol.Wild, 7),
    ];

    // Fish value multipliers (× total bet).
    private static readonly (int Item, int Weight)[] FishValues =
    [
        (2, 30), (5, 25), (10, 18), (15, 10), (20, 7), (25, 5), (50, 3), (100, 2),
    ];

    private readonly GameInfo _info = new(
        Bets,
        Paylines,
        Paytable.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        ScatterAwards.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        Multipliers,
        WildsPerRetrigger);

    public GameInfo Info() => _info;

    public GameState Snapshot(GameSession s) => new(
        Round(s.Balance),
        s.FreeSpinsRemaining,
        s.FreeSpinsTotal,
        s.Multiplier,
        s.WildsCollected,
        s.Retriggers,
        Round(s.FeatureWin),
        Round(s.FreeSpinBet));

    /// <summary>Returns an error message, or null if the spin may proceed.</summary>
    public string? Validate(GameSession s, decimal? bet)
    {
        if (s.FreeSpinsRemaining > 0)
            return null; // bet is ignored during free spins
        if (bet is not { } b || !Bets.Contains(b))
            return "Invalid bet.";
        if (s.Balance < b)
            return "Insufficient balance.";
        return null;
    }

    public SpinResult Spin(GameSession s, decimal? bet)
    {
        if (Validate(s, bet) is { } error)
            throw new InvalidOperationException(error);

        var free = s.FreeSpinsRemaining > 0;
        decimal stake;
        if (free)
        {
            stake = s.FreeSpinBet;
            s.FreeSpinsRemaining--;
        }
        else
        {
            stake = bet!.Value;
            // Feature summary from a finished feature stays visible until the next paid spin.
            s.ClearFeature();
            s.Balance -= stake;
        }

        var grid = Generate(free, stake);
        var lineWins = EvaluateLines(grid, stake);
        var lineTotal = lineWins.Sum(w => w.Win);

        var cells = grid.SelectMany(reel => reel).ToList();
        var scatterCount = cells.Count(c => c.Symbol == Symbol.Scatter);
        var wildCount = cells.Count(c => c.Symbol == Symbol.Wild);

        decimal scatterWin = 0;
        var freeSpinsAwarded = 0;
        if (!free && ScatterAwards.TryGetValue(scatterCount, out var award))
        {
            scatterWin = Round(award.Pay * stake);
            freeSpinsAwarded = award.FreeSpins;
        }

        decimal collectWin = 0;
        var retriggered = false;
        if (free && wildCount > 0)
        {
            var fishSum = cells.Where(c => c.Symbol == Symbol.Fish).Sum(c => c.Value ?? 0m);
            // Uses the multiplier in effect before this spin's retrigger.
            collectWin = Round(fishSum * wildCount * s.Multiplier);

            s.WildsCollected += wildCount;
            while (s.Retriggers < Multipliers.Length - 1
                   && s.WildsCollected >= WildsPerRetrigger * (s.Retriggers + 1))
            {
                s.Retriggers++;
                s.Multiplier = Multipliers[s.Retriggers];
                s.FreeSpinsRemaining += RetriggerSpins;
                s.FreeSpinsTotal += RetriggerSpins;
                retriggered = true;
            }
        }

        var totalWin = Round(lineTotal + scatterWin + collectWin);
        s.Balance = Round(s.Balance + totalWin);

        var featureEnded = false;
        if (free)
        {
            s.FeatureWin = Round(s.FeatureWin + totalWin);
            featureEnded = s.FreeSpinsRemaining == 0;
        }

        if (freeSpinsAwarded > 0)
        {
            s.ClearFeature();
            s.FreeSpinsRemaining = freeSpinsAwarded;
            s.FreeSpinsTotal = freeSpinsAwarded;
            s.FreeSpinBet = stake;
        }

        return new SpinResult(
            grid, Round(stake), free, lineWins,
            scatterCount, scatterWin, wildCount, collectWin, totalWin,
            freeSpinsAwarded, retriggered, featureEnded, Snapshot(s));
    }

    public GameState Reset(GameSession s)
    {
        s.Balance = StartingBalance;
        s.ClearFeature();
        return Snapshot(s);
    }

    private static Cell[][] Generate(bool free, decimal stake)
    {
        var grid = new Cell[Reels][];
        for (var r = 0; r < Reels; r++)
        {
            var reel = new Cell[Rows];
            var scatterRow = !free && RandomNumberGenerator.GetInt32(1000) < ScatterChancePerMille
                ? RandomNumberGenerator.GetInt32(Rows)
                : -1;
            for (var row = 0; row < Rows; row++)
            {
                reel[row] = row == scatterRow
                    ? new Cell(Symbol.Scatter, null)
                    : MakeCell(Pick(free ? FreeWeights : BaseWeights), stake);
            }
            grid[r] = reel;
        }
        return grid;
    }

    private static Cell MakeCell(Symbol symbol, decimal stake) =>
        symbol == Symbol.Fish
            ? new Cell(symbol, Round(Pick(FishValues) * stake))
            : new Cell(symbol, null);

    private static List<LineWin> EvaluateLines(Cell[][] grid, decimal stake)
    {
        var lineBet = stake / LinesCount;
        List<LineWin> wins = [];
        for (var i = 0; i < Paylines.Length; i++)
        {
            var line = Paylines[i];
            var symbols = new Symbol[Reels];
            for (var r = 0; r < Reels; r++)
                symbols[r] = grid[r][line[r]].Symbol;

            if (BestRun(symbols) is not { } run)
                continue;

            var win = Round(Pay(run.Symbol, run.Count) * lineBet);
            int[][] positions = [.. Enumerable.Range(0, run.Count).Select(r => new[] { r, line[r] })];
            wins.Add(new LineWin(i, run.Symbol, run.Count, win, positions));
        }
        return wins;
    }

    /// <summary>Best left-to-right run on a line: either a pure-Wild run or a symbol run with Wild substitution.</summary>
    private static (Symbol Symbol, int Count)? BestRun(Symbol[] s)
    {
        var wildRun = 0;
        while (wildRun < s.Length && s[wildRun] == Symbol.Wild)
            wildRun++;

        (Symbol Symbol, int Count)? best = wildRun >= 3 ? (Symbol.Wild, wildRun) : null;
        if (wildRun == s.Length)
            return best;

        var target = s[wildRun];
        var count = 0;
        if (target == Symbol.Fish)
        {
            // Wild does not substitute for Fish, so a Fish run must start on reel 1.
            if (wildRun == 0)
                while (count < s.Length && s[count] == Symbol.Fish)
                    count++;
        }
        else if (target != Symbol.Scatter)
        {
            count = wildRun;
            while (count < s.Length && (s[count] == target || s[count] == Symbol.Wild))
                count++;
        }

        if (count >= 3 && (best is not { } b || Pay(target, count) > Pay(b.Symbol, b.Count)))
            best = (target, count);
        return best;
    }

    private static decimal Pay(Symbol symbol, int count) => Paytable[symbol][count - 3];

    private static T Pick<T>((T Item, int Weight)[] table)
    {
        var total = 0;
        foreach (var entry in table)
            total += entry.Weight;

        var roll = RandomNumberGenerator.GetInt32(total);
        foreach (var (item, weight) in table)
        {
            if (roll < weight)
                return item;
            roll -= weight;
        }
        return table[^1].Item;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
