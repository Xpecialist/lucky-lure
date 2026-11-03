namespace MyApp.Game;

/// <summary>Mutable per-player state. Callers must lock the instance while reading or mutating.</summary>
public sealed class GameSession(string id)
{
    public string Id { get; } = id;
    public decimal Balance { get; set; } = SlotEngine.StartingBalance;

    public int FreeSpinsRemaining { get; set; }
    public int FreeSpinsTotal { get; set; }
    public decimal FreeSpinBet { get; set; }
    public int Multiplier { get; set; } = 1;
    public int WildsCollected { get; set; }
    public int Retriggers { get; set; }
    public decimal FeatureWin { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    /// <summary>True once the player has spun; unplayed sessions expire sooner.</summary>
    public bool HasSpun { get; set; }

    public void ClearFeature()
    {
        FreeSpinsRemaining = 0;
        FreeSpinsTotal = 0;
        FreeSpinBet = 0;
        Multiplier = 1;
        WildsCollected = 0;
        Retriggers = 0;
        FeatureWin = 0;
    }
}
