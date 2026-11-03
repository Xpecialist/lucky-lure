namespace MyApp.Client.Services;

/// <summary>A failed API call. <see cref="IsNetwork"/> is true when the server could not be reached.</summary>
public sealed class ApiException(string message, bool isNetwork = false) : Exception(message)
{
    public bool IsNetwork { get; } = isNetwork;
}
