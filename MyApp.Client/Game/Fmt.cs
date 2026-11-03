using System.Globalization;

namespace MyApp.Client.Game;

/// <summary>Money formatting (play money, 2 decimals, invariant culture).</summary>
public static class Fmt
{
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Compact form for fish badges: "5" or "0.50".</summary>
    public static string Short(decimal value)
    {
        var v = Math.Round(value, 2);
        return v == decimal.Truncate(v)
            ? decimal.Truncate(v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
