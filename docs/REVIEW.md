# Code review findings

Review of `main` @ `700332f` (2026-11-03) by the reviewer agent. No Critical issues; the engine matches
`API_CONTRACT.md`. Update the **Status** column as items are fixed (add the commit).

| ID | Sev | Area | Where | Issue | Suggested fix | Status |
|---|---|---|---|---|---|---|
| G1 | High | Game math | `MyApp/Game/SlotEngine.cs` | RTP ≈ 1129%: free-spin feature worth ~2000× bet | Monte-Carlo sim + retune non-contract weights | **Fixed** (rebalance commit, see DEVLOG) |
| S1 | High | Robustness | `MyApp/Program.cs:21-42`, `Game/SessionStore.cs` | Every cookie-less `/api/*` request (even `/api/info`, 404s) creates a 24 h session; no cap or rate limit. Also why first page load creates 2 sessions | Route group + endpoint filter only on `/state`, `/spin`, `/reset`; `AddRateLimiter` per IP; shorter TTL for never-spun sessions | **Fixed** (S1/S2 commit): `MapGroup` + endpoint filter, token-bucket 20 burst / 10 per s per IP → 429, never-spun TTL 30 min. TTL test blocked on T1 |
| H1 | Medium | Hosting | `MyApp.Client/MyApp.Client.csproj`, `wwwroot/index.html` | `dotnet publish MyApp` serves raw index.html with `#[.{fingerprint}]` placeholder (processed file isn't passed to the host on publish) | A: `OverrideHtmlAssetPlaceholders=false`, `WasmFingerprintAssets=false`, plain `_framework/blazor.webassembly.js` tag, drop importmap/preload. B: convert to .NET 10 Blazor Web App host (`App.razor`, `<ImportMap/>`, `@Assets`) | Open |
| T1 | Medium | Testability | `SlotEngine.cs` (static RNG), `SessionStore.cs` (`DateTime.UtcNow`) | No deterministic tests possible | Grid seam (`internal Apply(...)` + `InternalsVisibleTo`), `IRandomSource` injected, `TimeProvider` in SessionStore | Open |
| S2 | Low | HTTP | `Program.cs:73` | Wrong verb on `/api/*` → 404 instead of 405 (`/api` fallback has no method metadata) | Remove `/api` fallback; SPA fallback pattern excluding `api`: `{*path:nonfile:regex(^(?!api(/|$)).*$)}` | **Fixed** (S1/S2 commit). The suggested pattern didn't work on .NET 10; used `DisableStaticAssetNotFoundRuntimeFallback` + two fallbacks (`"/"` and `{first:regex(^(?!api$)[^.]*$)}/{*rest:nonfile}`) |
| S3 | Low | Errors | `Program.cs:53-63` | Malformed body → 400 without `{error}`; exceptions → bare 500 | `AddProblemDetails` + exception handler writing `{ error }` | Open |
| S4 | Low | Security | `Program.cs:35` | `Secure = IsHttps` fails behind TLS proxy; no HSTS/HTTPS redirect | Secure outside Development, `UseForwardedHeaders`, `UseHsts`, `UseHttpsRedirection`; optional `__Host-` cookie | Open |
| C1 | Low | Blazor async | `Home.razor.cs:153-183, 289, 302` | Unexpected exception leaves `busy=true` (buttons dead); fire-and-forget `SpinLaterAsync` swallows errors | `try/finally`, catch-all → error UI | Open |
| C2 | Low | Race | `Home.razor.cs:312-324` | Turning Auto off doesn't cancel overlay auto-dismiss | `CancelOverlayTimer()` in the off branch | Open |
| C3 | Low | Race | `Home.razor:63`, `Home.razor.cs:138` | Spin allowed during Reset | Guard + disable on `resetting` | Open |
| C4 | Low | Errors | `Home.razor.cs:181`, `Client/Program.cs:11` | No state refresh after network error; 100 s default HttpClient timeout; no CancellationToken | Always refresh state; `Timeout = 10s`; token cancelled in `DisposeAsync` | Open |
| C5 | Low | CSS | `wwwroot/css/app.css:132-159` | Bet shows "1...." at ~375 px | `@media (max-width:420px)` tighter bet-row/buttons, wider middle column | Open |
| C6 | Nit | Art | `Game/Symbols.cs:22,24` | 🛟 blank on older Windows; 🪰 is a fly, not a dragonfly | SVG/PNG symbol art (deferred by user) | Deferred |
| C7 | Nit | JS interop | `wwwroot/js/keys.js:12-15` | Held Space/Enter skips overlays | `if (!e.repeat)` on overlay branch | Open |
| C8 | Nit | Disposal | `Home.razor.cs:91-131` | Disposed-during-import leaks listener; catches `JSDisconnectedException` (Server-only) | Check `disposed` after awaits; catch `JSException` | Open |
| Q1 | Low | Duplication | client + server | Reels/Rows/Lines/fish values/+10 spins duplicated; client re-derives server values | `GameRules` in Shared; add `retriggerSpins`/`fishValues` to `GameInfo` (contract change) | Open |
| Q2 | Nit | Idioms | `Program.cs`, `SlotEngine.cs` | `lock` on public object; Validate+Spin double check; `GameInfo` exposes mutable static arrays | `System.Threading.Lock` field; `TrySpin`; defensive copies / `FrozenDictionary` | Open |
| Q3 | Nit | Hygiene | README, client csproj | README status stale; unused client launchSettings + DevServer package | Update/remove | README updated; rest open |

## Recommended order
1. Backend batch 1: ~~S1~~ → ~~S2~~ → T1 → S3 → S4.
2. Tester: **done for public-API tests** (`tests/MyApp.Tests`, 69 pass, 2 skipped = S2/S3).
   After T1: add the fixed-grid rule tests listed in DEVLOG.
3. Backend batch 2: Q2. (G1 done.)
4. Frontend batch 1: H1 (fix A) → C5 → C1 → C2 → C3 → C7.
5. Frontend batch 2: C4, C8; Q1 (both sides + contract proposal); Q3.
