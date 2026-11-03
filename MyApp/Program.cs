using System.Text.Json.Serialization;
using MyApp.Game;
using MyApp.Shared;

const string CookieName = "lucky_lure_sid";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SlotEngine>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Lets the browser debug the C# client code while developing.
if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

// Attach (or create) the player's session for every API request.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
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
    }
    await next(ctx);
});

app.MapGet("/api/info", (SlotEngine engine) => engine.Info());

app.MapGet("/api/state", (HttpContext ctx, SlotEngine engine) =>
{
    var session = SessionOf(ctx);
    lock (session)
        return engine.Snapshot(session);
});

app.MapPost("/api/spin", (SpinRequest? body, HttpContext ctx, SlotEngine engine) =>
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

app.MapPost("/api/reset", (HttpContext ctx, SlotEngine engine) =>
{
    var session = SessionOf(ctx);
    lock (session)
        return engine.Reset(session);
});

// Unknown /api/* paths are a plain 404, never the client page.
app.MapFallback("/api/{**path}", () => Results.NotFound());

// The Blazor client: its static files (fingerprinted _framework/*, css, js), and index.html
// for every other route so client-side navigation and page reloads work.
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();

static GameSession SessionOf(HttpContext ctx) => (GameSession)ctx.Items[typeof(GameSession)]!;
