using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Tests.Api;

/// <summary>Error handling (REVIEW S3) and deployment hardening (REVIEW S4), each on its own configured server.</summary>
public sealed class HostingTests : IDisposable
{
    private const string CookieName = "lucky_lure_sid";
    private readonly List<IDisposable> _factories = [];

    private WebApplicationFactory<Program> Server(Action<IWebHostBuilder> configure)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(configure);
        _factories.Add(factory);
        return factory;
    }

    [Fact]
    public async Task UnhandledException_Returns500JsonError_WithoutDetails()
    {
        // The RNG seam lets us make the engine blow up mid-spin.
        var client = Server(b => b.ConfigureTestServices(s => s.AddSingleton<IRandomSource, ThrowingRandom>())).CreateClient();

        var response = await client.PostAsJsonAsync("/api/spin", new SpinRequest(1m));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
        Assert.DoesNotContain("boom", body);         // no exception message or stack trace leaks
        Assert.DoesNotContain(" at ", body);
    }

    [Fact]
    public async Task Production_SessionCookieIsSecure()
    {
        var client = Server(b => b.UseEnvironment("Production"))
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/state");

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "No Set-Cookie header.");
        Assert.Contains("secure", Assert.Single(values, v => v.StartsWith(CookieName + "=")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BehindALocalProxy_RateLimitPartitionsByForwardedClientIp()
    {
        var client = Server(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("RateLimit:Burst", "1");
            b.UseSetting("RateLimit:PerSecond", "1");
            // TestServer has no remote IP; pretend every request arrives from a proxy on this machine.
            b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter, FromLoopback>());
        }).CreateClient();

        Task<HttpResponseMessage> StateFor(string clientIp)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/state");
            request.Headers.Add("X-Forwarded-For", clientIp);
            request.Headers.Add("X-Forwarded-Proto", "https");
            return client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await StateFor("203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StateFor("203.0.113.1")).StatusCode); // same client: bucket empty
        Assert.Equal(HttpStatusCode.OK, (await StateFor("203.0.113.2")).StatusCode);              // other client: own bucket
    }

    public void Dispose() => _factories.ForEach(f => f.Dispose());

    private sealed class ThrowingRandom : IRandomSource
    {
        public int Next(int maxExclusive) => throw new InvalidOperationException("boom");
    }

    private sealed class FromLoopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }
}
