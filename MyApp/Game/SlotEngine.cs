using System.Security.Cryptography;
using MyApp.Shared;

namespace MyApp.Game;

// Economy (tuned with tools/LuckyLure.Sim, 20,000,000 paid spins at bet 1, features played out):
//   free-spin trigger  1 in 50.2 paid spins (54.6% of triggers come via a nudge respin)
//   nudge respins 3.95 per 100 paid spins; 32.5% of nudge sequences end in a trigger
//   RTP 95.35%  = base lines 38.12% (incl. respin lines) + scatter pays 5.52% + feature 51.71% (54.2% of RTP)
//   avg feature win 26.0x bet (avg 12.9 free spins); retrigger in 18.0% of features,
//   x3 reached in 5.3%, x10 in 1.8%; base hit frequency 20.7%.
// Re-run the sim after changing any weight, pay or chance below.
public sealed class SlotEngine
{
    public const decimal StartingBalance = 1000m;

    private const int Reels = 5;
    private const int Rows = 3;
    private const int LinesCount = 10;
    private const int WildsPerRetrigger = 4;
    private const int RetriggerSpins = 10;
    // Chance per reel (out of 1000) that a Scatter lands in the base game.
    // At 10.2%: P(3+ of 5) ≈ 0.91% directly, plus ≈ 1.09% via nudge respins ≈ 2.0% ≈ 1 in 50 paid spins.
    private const int ScatterChancePerMille = 102;
    // Nudge respins per paid spin. Only two held Scatters that both started in row 0 can qualify twice.
    private const int MaxRespins = 2;

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
        [Symbol.Ten] = [7, 25, 100],
        [Symbol.Jack] = [7, 25, 100],
        [Symbol.Queen] = [10, 40, 150],
        [Symbol.King] = [15, 50, 200],
        [Symbol.Ace] = [20, 60, 250],
        [Symbol.TackleBox] = [40, 150, 500],
        [Symbol.Dragonfly] = [50, 200, 750],
        [Symbol.Rod] = [60, 250, 1000],
        [Symbol.Float] = [75, 300, 1500],
        [Symbol.Fish] = [40, 150, 750],
        [Symbol.Wild] = [100, 500, 2000],
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
        (Symbol.Ten, 50), (Symbol.Jack, 50), (Symbol.Queen, 40), (Symbol.King, 34), (Symbol.Ace, 28),
        (Symbol.TackleBox, 12), (Symbol.Dragonfly, 10), (Symbol.Rod, 8), (Symbol.Float, 6), (Symbol.Fish, 8),
    ];

    private static readonly (Symbol Item, int Weight)[] FreeWeights =
    [
        (Symbol.Ten, 100), (Symbol.Jack, 100), (Symbol.Queen, 76), (Symbol.King, 64), (Symbol.Ace, 45),
        (Symbol.TackleBox, 22), (Symbol.Dragonfly, 20), (Symbol.Rod, 16), (Symbol.Float, 12), (Symbol.Fish, 38),
        (Symbol.Wild, 7),
    ];

    // Fish value multipliers (× total bet).
    private static readonly (int Item, int Weight)[] FishValues =
    [
        (2, 600), (5, 250), (10, 90), (15, 30), (20, 15), (25, 10), (50, 4), (100, 1),
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

        s.HasSpun = true;
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

        // Nudge respins (base game only): line wins pay on every grid, scatters count on the final one.
        List<RespinStep> respins = [];
        var final = grid;
        while (!free && respins.Count < MaxRespins && NudgeReels(final) is { } held)
        {
            final = Nudge(final, held, stake);
            var wins = EvaluateLines(final, stake);
            lineTotal += wins.Sum(w => w.Win);
            respins.Add(new RespinStep(final, held, wins));
        }

        var cells = grid.SelectMany(reel => reel).ToList();
        var scatterCount = final.Sum(reel => reel.Count(c => c.Symbol == Symbol.Scatter));
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
            freeSpinsAwarded, retriggered, featureEnded, respins, Snapshot(s));
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
            grid[r] = GenerateReel(free, stake);
        return grid;
    }

    private static Cell[] GenerateReel(bool free, decimal stake)
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
        return reel;
    }

    /// <summary>The 2 scatter reels if the grid qualifies for a nudge (exactly 2 Scatters, both in row 0 or 1), else null.</summary>
    private static int[]? NudgeReels(Cell[][] grid)
    {
        List<int> reels = [];
        for (var r = 0; r < Reels; r++)
        {
            for (var row = 0; row < Rows; row++)
            {
                if (grid[r][row].Symbol != Symbol.Scatter)
                    continue;
                if (row == Rows - 1)
                    return null; // a Scatter in the bottom row can't be nudged
                reels.Add(r);
            }
        }
        return reels.Count == 2 ? [.. reels] : null;
    }

    /// <summary>Held reels shift down one row with a new non-Scatter symbol on top; the others respin normally.</summary>
    private static Cell[][] Nudge(Cell[][] grid, int[] held, decimal stake)
    {
        var next = new Cell[Reels][];
        for (var r = 0; r < Reels; r++)
        {
            next[r] = held.Contains(r)
                ? [MakeCell(Pick(BaseWeights), stake), grid[r][0], grid[r][1]] // BaseWeights has no Scatter
                : GenerateReel(free: false, stake);
        }
        return next;
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
