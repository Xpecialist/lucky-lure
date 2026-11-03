using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyApp.Tests.Api;

/// <summary>Its own server with a tiny token bucket (burst 3, refill 1/s), so the limit trips in a few calls.</summary>
public sealed class RateLimitTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimit:Burst", "3");
            b.UseSetting("RateLimit:PerSecond", "1");
        });

    [Fact]
    public async Task SessionEndpoints_AfterTheBurst_Return429WithJsonError()
    {
        var client = _factory.CreateClient();

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 10 && rejected is null; i++)
        {
            var response = await client.PostAsJsonAsync("/api/spin", new SpinRequest(1m));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
        }

        Assert.NotNull(rejected);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("Too many requests. Slow down.", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Info_IsNotRateLimited()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/info")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
