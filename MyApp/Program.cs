using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using MyApp.Game;
using MyApp.Shared;

const string CookieName = "lucky_lure_sid";
const string SessionPolicy = "session";

var builder = WebApplication.CreateBuilder(args);

// In Development, MapStaticAssets adds a GET catch-all ({**path:file}) for files created after the build.
// It would also catch GET /api/spin and turn the 405 into a 404, so switch it off (rebuild picks up new files).
builder.Configuration["DisableStaticAssetNotFoundRuntimeFallback"] = "true";

// Seams: tests swap these for a fake clock or a scripted RNG.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRandomSource, CryptoRandomSource>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SlotEngine>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Errors are always JSON { "error": ... }: unreadable request bodies throw (instead of an empty 400)
// so the exception handler below can answer them, and unhandled exceptions get a generic 500.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<JsonErrorHandler>();

// Behind a reverse proxy (TLS terminated there), trust X-Forwarded-For/-Proto so the rate limiter sees the real
// client IP and Request.IsHttps is true. Only proxies listed here are trusted (default: loopback, i.e. a proxy on
// the same machine); add others with "ForwardedHeaders:KnownProxies" (IPs) or ":KnownNetworks" (CIDR).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var section = builder.Configuration.GetSection("ForwardedHeaders");
    foreach (var ip in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(IPAddress.Parse(ip));
    foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

// Per-IP token bucket on the session endpoints. A real player makes at most a few calls a second
// (free spins auto-play about once a second; mashing Space is maybe 5-8 spins a second), so a burst of 20
// refilled at 10 per second never gets in their way, while a script can't mint sessions in bulk.
// Limits can be overridden in configuration ("RateLimit:Burst", "RateLimit:PerSecond").
builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection("RateLimit"));
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(SessionPolicy, ctx =>
    {
        var limits = ctx.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        return RateLimitPartition.GetTokenBucketLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limits.Burst,
                TokensPerPeriod = limits.PerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
            });
    });
    o.OnRejected = async (rejected, ct) =>
    {
        var response = rejected.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(1);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await response.WriteAsJsonAsync(new { error = "Too many requests. Slow down." }, ct);
    };
});

var app = builder.Build();

app.UseForwardedHeaders(); // first, so everything after sees the real scheme and client IP
app.UseExceptionHandler(); // runs JsonErrorHandler; also replaces the HTML developer error page

if (app.Environment.IsDevelopment())
{
    // Lets the browser debug the C# client code while developing.
    app.UseWebAssemblyDebugging();
}
else
{
    // Deployed: tell browsers to stay on HTTPS, and redirect plain HTTP there.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.MapGet("/api/info", (SlotEngine engine) => engine.Info());

// Endpoints that need the player's session: rate limited, and the filter attaches (or creates) the session.
var play = app.MapGroup("/api")
    .RequireRateLimiting(SessionPolicy)
    .AddEndpointFilter(AttachSession);

play.MapGet("/state", (HttpContext ctx, SlotEngine engine) =>
{
    var session = SessionOf(ctx);
    lock (session)
        return engine.Snapshot(session);
});

play.MapPost("/spin", (SpinRequest? body, HttpContext ctx, SlotEngine engine) =>
{
    var session = SessionOf(ctx);
    lock (session)
    {
        var error = engine.Validate(session, body?.Bet);
        if (error is not null)
            return Results.BadRequest(new { error });
        return Results.Ok(engine.Spin(session, body?.Bet));
    }
});

play.MapPost("/reset", (HttpContext ctx, SlotEngine engine) =>
{
    var session = SessionOf(ctx);
    lock (session)
        return engine.Reset(session);
});

// The Blazor client: its static files (fingerprinted _framework/*, css, js), and index.html for "/" and
// every other route except /api/*, so client-side navigation works while unknown API paths stay a plain
// 404 and a wrong verb on a real API path stays 405. The regex sits on the first segment (not on a
// catch-all) so routing can rule out the literal "api" segment up front, before the HTTP-method check.
app.MapStaticAssets();
app.MapFallbackToFile("/", "index.html");
app.MapFallbackToFile("{first:regex(^(?!api$)[^.]*$)}/{*rest:nonfile}", "index.html");

app.Run();

static GameSession SessionOf(HttpContext ctx) => (GameSession)ctx.Items[typeof(GameSession)]!;

static ValueTask<object?> AttachSession(EndpointFilterInvocationContext fc, EndpointFilterDelegate next)
{
    var ctx = fc.HttpContext;
    var store = ctx.RequestServices.GetRequiredService<SessionStore>();
    var session = store.Get(ctx.Request.Cookies[CookieName]);
    if (session is null)
    {
        session = store.Create();
        ctx.Response.Cookies.Append(CookieName, session.Id, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            // Always Secure when deployed (HTTPS, possibly via a proxy); in Development only when actually on HTTPS,
            // so http://localhost keeps working.
            Secure = ctx.Request.IsHttps || !ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            Path = "/",
        });
    }
    ctx.Items[typeof(GameSession)] = session;
    return next(fc);
}

/// <summary>Turns any exception into JSON { "error": ... }: 4xx for bad requests (e.g. malformed JSON), else a generic 500.</summary>
sealed class JsonErrorHandler(ILogger<JsonErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var (status, error) = exception is BadHttpRequestException bad
            ? (bad.StatusCode, bad.StatusCode == StatusCodes.Status400BadRequest ? "Invalid request body." : "Invalid request.")
            : (StatusCodes.Status500InternalServerError, "Something went wrong. Please try again.");
        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { error }, ct); // no details or stack trace leave the server
        return true;
    }
}

sealed class RateLimitSettings
{
    public int Burst { get; set; } = 20;
    public int PerSecond { get; set; } = 10;
}
