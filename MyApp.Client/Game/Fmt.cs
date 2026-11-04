using System.Globalization;

namespace MyApp.Client.Game;

/// <summary>Money formatting (play money, 2 decimals, invariant culture).</summary>
public static class Fmt
{
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
