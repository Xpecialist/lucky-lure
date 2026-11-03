using System.Text.Json;
using System.Text.Json.Serialization;
using MyApp.Shared;

namespace MyApp.Client.Services;

/// <summary>Body of a 400 response: <c>{ "error": "..." }</c>.</summary>
public sealed record ApiErrorBody(string? Error);

/// <summary>
/// Source-generated JSON metadata (camelCase web defaults): trim-safe and avoids reflection
/// warm-up on the first spin.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GameInfo))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(SpinResult))]
[JsonSerializable(typeof(SpinRequest))]
[JsonSerializable(typeof(ApiErrorBody))]
internal sealed partial class GameJsonContext : JsonSerializerContext;
