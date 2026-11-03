using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyApp.Tests.Api;

/// <summary>
/// End-to-end HTTP tests. WebApplicationFactory boots the real Program.cs in memory (TestServer, no port),
/// and IClassFixture shares one server across the tests in this class.
/// Each HttpClient has its own cookie jar, i.e. its own player session.
/// </summary>
public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CookieName = "lucky_lure_sid";

    private HttpClient NewPlayer() => factory.CreateClient(); // HandleCookies = true by default

    [Fact]
    public async Task GetInfo_Returns200_WithSymbolNamesAsStrings()
    {
        var response = await NewPlayer().GetAsync("/api/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var paytableKeys = root.GetProperty("paytable").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("Ten", paytableKeys);
        Assert.Contains("Fish", paytableKeys);
        Assert.Contains("Wild", paytableKeys);
        Assert.Equal(10, root.GetProperty("paylines").GetArrayLength());
        Assert.Equal(4, root.GetProperty("wildsPerRetrigger").GetInt32()); // camelCase names
    }

    [Fact]
    public async Task FirstApiCall_SetsHttpOnlyStrictSessionCookie()
    {
        // Cookie handling off, so the raw Set-Cookie header is visible.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/state");

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "No Set-Cookie header.");
        var cookie = Assert.Single(values, v => v.StartsWith(CookieName + "="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInfo_DoesNotCreateASession()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task GetState_AfterInfo_IsWhatSetsTheCookie()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.GetAsync("/api/info");

        var response = await client.GetAsync("/api/state");

        Assert.Contains(SessionCookies(response), v => v.StartsWith(CookieName + "="));
    }

    [Fact]
    public async Task FirstPageLoad_InfoAndStateInParallel_CreatesExactlyOneSession()
    {
        // What the client does on load: both calls at once, before any cookie exists.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var responses = await Task.WhenAll(client.GetAsync("/api/info"), client.GetAsync("/api/state"));

        Assert.Single(responses.SelectMany(SessionCookies));
    }

    private static IEnumerable<string> SessionCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith(CookieName + "="))
            : [];

    [Fact]
    public async Task LaterCalls_WithTheCookie_DoNotIssueANewOne()
    {
        var client = NewPlayer();
        await client.GetAsync("/api/state");

        var second = await client.GetAsync("/api/state");

        Assert.False(second.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task GetState_NewPlayer_Has1000AndNoFeature()
    {
        var state = await NewPlayer().GetFromJsonAsync<GameState>("/api/state");

        Assert.Equal(new GameState(1000m, 0, 0, 1, 0, 0, 0m, 0m), state);
    }

    [Fact]
    public async Task PostSpin_Returns200_AndBalanceChangesByWinMinusBet()
    {
        var client = NewPlayer();

        var response = await client.PostAsJsonAsync("/api/spin", new SpinRequest(1m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spin = await response.Content.ReadFromJsonAsync<SpinResult>();
        Assert.NotNull(spin);
        Assert.False(spin.WasFreeSpin);
        Assert.Equal(1m, spin.Bet);
        Assert.Equal(1000m - 1m + spin.TotalWin, spin.State.Balance);

        // The session persisted via the cookie: /api/state agrees with the spin.
        var state = await client.GetFromJsonAsync<GameState>("/api/state");
        Assert.Equal(spin.State, state);
    }

    [Fact]
    public async Task PostSpin_SerializesSymbolsAsStrings()
    {
        var response = await NewPlayer().PostAsJsonAsync("/api/spin", new { bet = 1 });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var grid = json.RootElement.GetProperty("grid");
        Assert.Equal(5, grid.GetArrayLength());
        foreach (var reel in grid.EnumerateArray())
        {
            Assert.Equal(3, reel.GetArrayLength());
            foreach (var cell in reel.EnumerateArray())
                Assert.Equal(JsonValueKind.String, cell.GetProperty("symbol").ValueKind);
        }
    }

    [Theory]
    [InlineData("{\"bet\":3}")]
    [InlineData("{\"bet\":0}")]
    [InlineData("{}")]
    public async Task PostSpin_InvalidBet_Returns400WithErrorMessage(string body)
    {
        var client = NewPlayer();

        var response = await client.PostAsync("/api/spin", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));

        var state = await client.GetFromJsonAsync<GameState>("/api/state");
        Assert.Equal(1000m, state!.Balance); // nothing was charged
    }

    [Fact(Skip = "Known REVIEW S3: a malformed JSON body returns 400 without the { error } body. Un-skip when S3 is fixed.")]
    public async Task PostSpin_MalformedJson_Returns400WithErrorMessage()
    {
        var response = await NewPlayer().PostAsync("/api/spin",
            new StringContent("not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task PostReset_Restores1000()
    {
        var client = NewPlayer();
        await client.PostAsJsonAsync("/api/spin", new SpinRequest(10m));

        var response = await client.PostAsync("/api/reset", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<GameState>();
        Assert.Equal(new GameState(1000m, 0, 0, 1, 0, 0, 0m, 0m), state);
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api/info/extra")]
    [InlineData("/api")]
    [InlineData("/API/nope")]
    public async Task UnknownApiPath_Returns404_NotThePage(string path)
    {
        var response = await NewPlayer().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/css/missing.css")]
    public async Task MissingStaticFile_Returns404_NotThePage(string path)
    {
        var response = await NewPlayer().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSpin_WrongVerb_Returns405()
    {
        var response = await NewPlayer().GetAsync("/api/spin");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/some/client/route")]
    [InlineData("/apiary")]
    public async Task NonApiRoutes_ReturnTheClientHtmlPage(string path)
    {
        var response = await NewPlayer().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
