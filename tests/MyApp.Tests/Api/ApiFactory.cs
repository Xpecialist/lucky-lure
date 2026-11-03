using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyApp.Tests.Api;

/// <summary>
/// The real app with the per-IP rate limit raised out of the way. Every TestServer request shares one
/// partition ("unknown" IP), so the production limits would trip across a fast test run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimit:Burst", "100000");
        builder.UseSetting("RateLimit:PerSecond", "100000");
    }
}
