namespace MyApp.Client.Game;

/// <summary>How big a win is relative to the bet; picks the celebration.</summary>
public enum WinTier { None, Small, Big, Mega }

public static class WinTiers
{
    /// <summary>Total win (incl. respins) at or above this multiple of the bet is a BIG WIN.</summary>
    public const decimal BigMultiple = 10m;

    /// <summary>... and at or above this multiple a MEGA WIN.</summary>
    public const decimal MegaMultiple = 50m;

    public static WinTier For(decimal totalWin, decimal bet)
    {
        if (totalWin <= 0) return WinTier.None;
        if (bet <= 0) return WinTier.Small;
        var multiple = totalWin / bet;
        return multiple >= MegaMultiple ? WinTier.Mega
            : multiple >= BigMultiple ? WinTier.Big
            : WinTier.Small;
    }
}
