using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MyApp.Shared;

namespace MyApp.Client.Services;

/// <summary>Typed wrapper over the Lucky Lure HTTP API (see MyApp/API_CONTRACT.md).</summary>
public sealed class GameClient(HttpClient http)
{
    private const int Reels = 5;
    private const int Rows = 3;
    private static readonly GameJsonContext Json = GameJsonContext.Default;

    public Task<GameInfo> GetInfoAsync() =>
        SendAsync(() => http.GetAsync("api/info"), Json.GameInfo);

    public Task<GameState> GetStateAsync() =>
        SendAsync(() => http.GetAsync("api/state"), Json.GameState);

    public Task<GameState> ResetAsync() =>
        SendAsync(() => http.PostAsync("api/reset", content: null), Json.GameState);

    public async Task<SpinResult> SpinAsync(decimal bet)
    {
        var result = await SendAsync(
            () => http.PostAsJsonAsync("api/spin", new SpinRequest(bet), Json.SpinRequest),
            Json.SpinResult);

        // Older servers / null JSON: treat a missing "respins" as none.
        result = result with
        {
            LineWins = result.LineWins ?? [],
            Respins = result.Respins ?? [],
        };

        if (!IsValidGrid(result.Grid) || result.State is null || result.Respins.Any(step => !IsValidStep(step)))
            throw new ApiException("Unexpected response from server.");
        return result;
    }

    private static bool IsValidGrid(Cell[][]? grid) =>
        grid is { Length: Reels } && grid.All(col => col is { Length: Rows });

    private static bool IsValidStep(RespinStep? step) =>
        step is not null && IsValidGrid(step.Grid) && step.LineWins is not null
        && step.HeldReels is { Length: > 0 } && step.HeldReels.All(r => r is >= 0 and < Reels);

    private static async Task<T> SendAsync<T>(Func<Task<HttpResponseMessage>> send, JsonTypeInfo<T> type)
    {
        try
        {
            using var response = await send();
            if (!response.IsSuccessStatusCode)
                throw new ApiException(await ReadErrorAsync(response));

            return await response.Content.ReadFromJsonAsync(type)
                   ?? throw new ApiException("Unexpected response from server.");
        }
        catch (ApiException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new ApiException("Unexpected response from server.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("Connection problem — could not reach the server. Please try again.", isNetwork: true);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync(Json.ApiErrorBody);
            if (!string.IsNullOrWhiteSpace(body?.Error)) return body.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not a JSON error body; fall through to the generic message.
        }
        return $"Server error ({(int)response.StatusCode}).";
    }
}
