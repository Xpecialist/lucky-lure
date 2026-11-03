using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using MyApp.Game;
using MyApp.Shared;

const string CookieName = "lucky_lure_sid";
const string SessionPolicy = "session";

var builder = WebApplication.CreateBuilder(args);

// In Development, MapStaticAssets adds a GET catch-all ({**path:file}) for files created after the build.
// It would also catch GET /api/spin and turn the 405 into a 404, so switch it off (rebuild picks up new files).
builder.Configuration["DisableStaticAssetNotFoundRuntimeFallback"] = "true";

builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SlotEngine>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

// Lets the browser debug the C# client code while developing.
if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

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
            Secure = ctx.Request.IsHttps,
            Path = "/",
        });
    }
    ctx.Items[typeof(GameSession)] = session;
    return next(fc);
}

sealed class RateLimitSettings
{
    public int Burst { get; set; } = 20;
    public int PerSecond { get; set; } = 10;
}
