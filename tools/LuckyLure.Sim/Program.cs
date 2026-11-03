// Monte-Carlo check of the Lucky Lure economy. Drives the real SlotEngine/GameSession:
// paid spins at bet 1, every triggered feature is played out to the end.
// Usage: dotnet run -c Release --project tools/LuckyLure.Sim [-- <paidSpins> [threads] [fast]]
// Default RNG is the production CryptoRandomSource; "fast" uses Random.Shared (same distribution, quicker).
using System.Diagnostics;
using MyApp.Game;

var spins = args.Length > 0 ? long.Parse(args[0]) : 2_000_000;
var threads = args.Length > 1 ? int.Parse(args[1]) : Environment.ProcessorCount;
var fast = args.Length > 2 && args[2] == "fast";
var engine = new SlotEngine(fast ? new SharedRandomSource() : new CryptoRandomSource());
var sw = Stopwatch.StartNew();

var parts = new Stats[threads];
Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
{
    var quota = spins / threads + (t < spins % threads ? 1 : 0);
    parts[t] = Run(engine, quota);
});
var total = parts.Aggregate(new Stats(), (a, b) => a.Add(b));
sw.Stop();

var staked = (decimal)total.PaidSpins; // bet 1 per paid spin
string Pct(decimal v) => $"{v * 100:0.00}%";
string Ratio(long n, long d) => d == 0 ? "n/a" : $"{(double)n / d * 100:0.00}%";

Console.WriteLine($"Paid spins          : {total.PaidSpins:N0}  (+{total.FreeSpins:N0} free spins, {threads} threads, {(fast ? "Random.Shared" : "crypto")} RNG, {sw.Elapsed.TotalSeconds:0.0}s)");
Console.WriteLine($"Features triggered  : {total.Features:N0}  = 1 in {(double)total.PaidSpins / Math.Max(1, total.Features):0.0} paid spins");
Console.WriteLine($"Nudge respins       : {(double)total.Respins / total.PaidSpins * 100:0.00} per 100 paid spins  ({Ratio(total.RespinSequences, total.PaidSpins)} of paid spins nudge)");
Console.WriteLine($"  sequences -> trig.: {Ratio(total.RespinTriggers, total.RespinSequences)}   triggers via respin: {Ratio(total.RespinTriggers, total.Features)}");
Console.WriteLine($"RTP total           : {Pct((total.BaseLineWin + total.ScatterWin + total.FeatureWin) / staked)}");
Console.WriteLine($"  base lines        : {Pct(total.BaseLineWin / staked)}");
Console.WriteLine($"  scatter pays      : {Pct(total.ScatterWin / staked)}");
Console.WriteLine($"  feature           : {Pct(total.FeatureWin / staked)}  (share of RTP {Pct(total.FeatureWin / Math.Max(1, total.BaseLineWin + total.ScatterWin + total.FeatureWin))})");
Console.WriteLine($"Avg feature win     : {total.FeatureWin / Math.Max(1, total.Features):0.00}x bet  (avg {(double)total.FreeSpins / Math.Max(1, total.Features):0.0} free spins)");
Console.WriteLine($"Features w/ retrig. : {Ratio(total.Retriggered, total.Features)}   reach x3: {Ratio(total.ReachedX3, total.Features)}   reach x10: {Ratio(total.ReachedX10, total.Features)}");
Console.WriteLine($"Features >= 100x    : {Ratio(total.Feature100, total.Features)}");
Console.WriteLine($"Max feature win     : {total.MaxFeatureWin:0.00}x bet");
Console.WriteLine($"Max single spin win : {total.MaxSpinWin:0.00}x bet");
Console.WriteLine($"Hit frequency (base): {Ratio(total.BaseHits, total.PaidSpins)}");

static Stats Run(SlotEngine engine, long quota)
{
    var st = new Stats();
    var s = new GameSession("sim") { Balance = 1_000_000_000_000m };
    for (long i = 0; i < quota; i++)
    {
        var r = engine.Spin(s, 1m);
        st.PaidSpins++;
        st.ScatterWin += r.ScatterWin;
        st.BaseLineWin += r.TotalWin - r.ScatterWin;
        if (r.TotalWin > 0) st.BaseHits++;
        st.MaxSpinWin = Math.Max(st.MaxSpinWin, r.TotalWin);
        st.Respins += r.Respins.Count;
        if (r.Respins.Count > 0)
        {
            st.RespinSequences++;
            if (r.FreeSpinsAwarded > 0) st.RespinTriggers++;
        }
        if (r.FreeSpinsAwarded == 0)
            continue;

        st.Features++;
        while (s.FreeSpinsRemaining > 0)
        {
            var f = engine.Spin(s, null);
            st.FreeSpins++;
            st.FeatureWin += f.TotalWin;
            st.MaxSpinWin = Math.Max(st.MaxSpinWin, f.TotalWin);
        }
        if (s.Retriggers >= 1) st.Retriggered++;
        if (s.Retriggers >= 2) st.ReachedX3++;
        if (s.Retriggers >= 3) st.ReachedX10++;
        if (s.FeatureWin >= 100) st.Feature100++;
        st.MaxFeatureWin = Math.Max(st.MaxFeatureWin, s.FeatureWin);
    }
    return st;
}

/// <summary>Thread-safe and fast, not cryptographic. Fine for measuring the math; production uses CryptoRandomSource.</summary>
sealed class SharedRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}

sealed class Stats
{
    public long PaidSpins, FreeSpins, Respins, RespinSequences, RespinTriggers, Features, Retriggered, ReachedX3, ReachedX10, Feature100, BaseHits;
    public decimal BaseLineWin, ScatterWin, FeatureWin, MaxSpinWin, MaxFeatureWin;

    public Stats Add(Stats o) => new()
    {
        PaidSpins = PaidSpins + o.PaidSpins, Respins = Respins + o.Respins, RespinSequences = RespinSequences + o.RespinSequences, RespinTriggers = RespinTriggers + o.RespinTriggers, FreeSpins = FreeSpins + o.FreeSpins, Features = Features + o.Features,
        Retriggered = Retriggered + o.Retriggered, ReachedX3 = ReachedX3 + o.ReachedX3, ReachedX10 = ReachedX10 + o.ReachedX10,
        Feature100 = Feature100 + o.Feature100, BaseHits = BaseHits + o.BaseHits,
        BaseLineWin = BaseLineWin + o.BaseLineWin, ScatterWin = ScatterWin + o.ScatterWin, FeatureWin = FeatureWin + o.FeatureWin,
        MaxSpinWin = Math.Max(MaxSpinWin, o.MaxSpinWin), MaxFeatureWin = Math.Max(MaxFeatureWin, o.MaxFeatureWin),
    };
}
