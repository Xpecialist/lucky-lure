# Dev log

Newest entries at the bottom. Each entry: what changed, why, and the commit.

## 2026-11-03 — Day 1

### Plan and contract
- Goal: a slot machine in the style of fishing-themed slots, built in ASP.NET, as a learning/CV project. Original name and
  look ("Lucky Lure"), no third-party slot names or assets, play money only.
- Architecture agreed with the user (diagrams shown in chat): browser UI → ASP.NET Core API → SessionStore + SlotEngine
  → shared models. Server-authoritative: the browser never decides outcomes.
- `MyApp/API_CONTRACT.md` written first so backend and frontend builders could work in parallel.

### Game rules (summary; full spec in API_CONTRACT.md)
- 5×3 reels, 10 paylines, left-to-right 3+. Line bet = bet/10. Bets 0.10–10, start balance 1000.
- Fish symbols carry cash values (2–100× bet). Wild = fisherman, only in free spins, substitutes except Fish/Scatter.
- 3/4/5 scatters (boats) → 10/15/20 free spins at the triggering bet.
- In free spins: collect = sum of fish values × number of wilds × multiplier. Every 4 wilds → +10 spins,
  multiplier 1 → 2 → 3 → 10 (from the next spin, max 3 retriggers).

### Builds
1. **First version:** backend (minimal APIs, engine, sessions) + plain HTML/CSS/JS frontend, written by two builder
   agents in parallel. No SDK installed yet, so code was written uncompiled.
2. **.NET 10 SDK installed** (10.0.403 via winget).
3. **Frontend switched to Blazor WebAssembly.** Reason: user wants .NET on the CV. Blazor Server and Razor Pages were
   rejected because every spin would wait on the network. Key requirement: spin responsiveness. The reels start the
   instant SPIN is clicked (pure CSS animation) while the request is in flight, then stop left to right on the
   server's result (min 350 ms spin, 130 ms between reel stops).
4. **Solution restructured:** `MyApp.sln` with MyApp (server), MyApp.Shared (shared records, `Symbol` serialized as a
   string via attribute), MyApp.Client (Blazor). Backend compiled clean after adding `using MyApp.Shared;`
   (17 × CS0246 from the namespace move).
5. **keys.js kept** (user decision): small JS interop for a page-wide Space key and the reduced-motion check.
6. **Project moved** from a temporary folder to `Desktop\LuckyLure` (short path fixes the Windows MAX_PATH build
   failures; the temporary `Directory.Build.props` workaround was removed).
7. **Git + GitHub:** Git 2.56 installed; repo pushed to github.com/Xpecialist/lucky-lure.
   Old plain-JS frontend deleted before the first commit (user request).

### Commits
- `2cea450` Initial commit: server, shared models, Blazor client.
- `a79f340` Server hosts the Blazor client (`MapStaticAssets` + `MapFallbackToFile("index.html")`, unknown
  `/api/*` → 404). Smoke-tested 10/10.
- `700332f` SPIN button beside the reels on wide landscape screens (≥ 900px); phones keep the stacked layout.

### Known issues found while playing
- Blank square on the reels = the Float symbol 🛟 (Emoji 14, not in this Windows 11 21H2 build's emoji font).
  **Deferred** by the user to a later art pass (plan: custom SVG symbol art).
- Bet value truncated to "1...." on phone widths (REVIEW.md C5).

### Code review
- Reviewer agent reviewed the whole codebase at `700332f`: no critical issues, engine matches the contract.
  Findings and status are tracked in `docs/REVIEW.md`.

### Economy rebalance (REVIEW G1)
- User request: bonus easier to reach (**1 in 50**) and far less rewarding.
- Backend builder added `tools/LuckyLure.Sim` and tuned only non-contract knobs in `SlotEngine.cs`
  (symbol weights, scatter chance 85‰ → 135‰ per reel, fish value weights, line paytable).
- Result (20M paid spins): trigger 1 in 50.3, RTP 95.65% (base lines 38.1%, scatter 5.4%, feature 52.2%),
  average feature 26.3× bet, retrigger in 17.8% of features, ×10 reached in 1.8%. Before: RTP ≈ 1129%.
  Coordinator re-ran 2M spins: 1 in 49.7, RTP 96.18%.

### Tests (tester agent)
- Added `tests/MyApp.Tests` (xUnit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.14). No production code changed.
- 71 tests: engine validation, info/reset, grid rules, 20k-spin accounting invariants, free-spin feature
  transitions (reference model in `SpinChecks.Transition`), line-win shape, API integration tests via
  `WebApplicationFactory<Program>` (cookie flags, 400 `{error}`, 404, `GET /` HTML), and a slow 1M-spin RTP test
  (`[Trait("Category","Slow")]`, last run RTP 94.87%, 1 in 50.1).
- Result: 69 passed, 0 failed, 2 skipped on purpose (S2 405, S3 malformed-JSON body; un-skip when fixed).
  No new bugs found.
- Blocked on T1 (needs fixed grids / `IRandomSource` / `TimeProvider`): exact paytable table, wild-substitution
  edge cases, multi-line totals, exact scatter pays, double retrigger in one spin, collect rounding,
  retrigger on last free spin, SessionStore pruning.

### Commits
- `666d803` xUnit test suite.

### S1 + S2 fixed (backend builder)
- S1: session middleware replaced by `MapGroup("/api")` (state/spin/reset) + endpoint filter; `/api/info` sets no
  cookie (fixes the "two sessions on first load" issue). Built-in rate limiter: token bucket per IP, burst 20,
  10/s refill, 429 + `Retry-After` + `{error}`; configurable via `RateLimit:Burst` / `RateLimit:PerSecond`.
  Never-spun sessions expire after 30 min (`GameSession.HasSpun`), played sessions 24 h.
- S2: wrong verb → 405. Needed `DisableStaticAssetNotFoundRuntimeFallback=true` (MapStaticAssets' Development
  catch-all swallowed 405) and a segment-level regex fallback instead of a catch-all regex.
- Tests: 82 total, 81 pass, 1 skipped (S3). New `ApiFactory` (raised limits) and `RateLimitTests`.
  Coordinator re-ran tests and played a spin in the browser: boots, no console errors.

### Commits
- `309f277` S1 + S2 fixes.

### Nudge respin (new feature, user design)
- Rule (base game only, inside one paid spin): exactly 2 Scatters, each in row 0 or 1 → the 2 scatter reels are
  held and shift down one row (new non-Scatter symbol on top), the other 3 reels respin. Repeats while the
  condition holds, cap 2 respins. Line wins pay on every grid; scatter pay/free spins from the final grid.
  User decisions: positions "each in top or middle", pay every step, keep 1-in-50 overall.
- Contract-first: coordinator added the rule to API_CONTRACT.md and `RespinStep` + `SpinResult.Respins` to
  Models.cs, then backend and frontend builders worked in parallel.
- Backend: nudge loop in `SlotEngine.Spin`; scatter chance 135‰ → 102‰, Ten/Jack 8/30/100 → 7/25/100.
  Sim 20M: trigger 1 in 50.2, RTP 95.35%, 3.36% of paid spins nudge, 32.5% of nudge sequences trigger,
  54.6% of all triggers come via a respin. Test invariants extended to every respin grid (84 tests, 83 pass, S3 skip).
- Frontend: `Reel` gets a `Held` parameter (gold frame + "HELD" tag, one-cell slide-down), sequence in
  `Pages/Home.Respins.cs`: original wins → boats glow + "2 Boats — nudge respin!" → held reels slide while the
  others spin → per-step wins accumulate → free-spins overlay if triggered.
- Coordinator verified: build clean, tests pass, 2M sim (1 in 50.0, RTP 95.71%), and saw a live nudge in the
  browser (message + 2 HELD tags, final grid correct). One mid-sequence screenshot showed reels half a cell off,
  taken while the browser pane was hidden (throttled animations); settled state was aligned. Worth a human look.

### Commits
- `7833d79` nudge respin.

### Parallel batch: bonus-flow bugs + Phase 1 visuals (frontend) and T1/S3/S4 (backend)
- Work split with a **git worktree**: backend builder in `Desktop\LuckyLure-backend` on branch
  `backend/t1-s3-s4` (own bin/obj, so no build locks), frontend builder in the main folder. Merged with
  `--no-ff` as `b07aff5`, no conflicts.
- Frontend `5504c54` — user-reported bugs: boats now celebrate ~1.3 s before the FREE SPINS overlay; page no
  longer jumps (banner inserted above the reels pushed them down — header/banner now share one fixed slot);
  overlays (free spins, retrigger "now ×2", feature complete) never auto-dismiss, wait for click/Space/Enter
  (`SkipGate` = TaskCompletionSource wait); retrigger overlay after the spin finishes. C5, C7 fixed.
  Phase 1: SVG sprite art for all 12 symbols (`SymbolArt.cs`, `SymbolSprite.razor`), payline glow trace, BIG WIN
  (≥ 10× bet, roll-up counter) / MEGA WIN (≥ 50×, coin+bubble shower), motion blur, stop bounce, SPIN glow;
  reduced-motion respected.
- Backend `5e2ba34` — T1: `IRandomSource`/`CryptoRandomSource` injected into `SlotEngine`, `internal Apply(...)`
  grid seam + `InternalsVisibleTo`, `TimeProvider` in `SessionStore`. S3: `{error}` JSON for every 400/500.
  S4: Secure cookie outside Dev, forwarded headers, HSTS + HTTPS redirect outside Dev. Sim gained `fast` mode.
- Coordinator verified after merge: build clean, **92/92 tests pass, 0 skipped**; live: new art renders (life ring
  visible), spin works, `{"bet":"abc"}` → 400 `{"error":"Invalid request body."}`, GET /api/spin → 405,
  no console errors. Animation smoothness not judged from tool screenshots (they can lag) — needs a human look.
- Note: a stray earlier draft of the sim change appeared in the main folder; dropped in favour of the branch.

### Commits
- `5504c54` frontend bugs + Phase 1, `5e2ba34` backend T1/S3/S4, merged `b07aff5`; `5666ddb` CLAUDE.md workflow.

### Net cast (new feature, user design) + "scatter looks wild" report
- Rule (free spins only): landed grid has ≥ 1 Wild and no Fish → 45% chance of a net cast; Small turns 3 non-Wild
  cells into Fish, Big turns 5 (Big = 1 in 5 casts). Fish values from their own net-cast weights. All wins of that
  spin evaluated on `net.grid`; top-level `grid` is the landed grid. Defaults chosen by coordinator ("big chance",
  "values by RTP" left to tuning by the user).
- Spec-first again: contract + `NetCast`/`NetSize` in Models.cs + `Net: null` placeholder in the engine committed
  on main (`5a0cc17`) so main kept building while the backend worked in the worktree (branch `backend/net-cast`).
- Backend `5d15a74`: `NetPlan` (decide) + `CastNet` (apply), `Apply(..., NetPlan?)` test seam. Normal fish weights
  cheapened (mean 4.65× → 3.30×) to make room; net weights mean 2.84×. Sim 20M: 1 in 50.1, RTP 95.43%, 6.26% of
  free spins qualify, 45% of those cast, avg collect per cast 15.6× bet, net casts = 23% of feature win.
- User report "scatter works as a wild": engine was correct; the Phase 1 payline glow was drawn across all 5 reels
  of the line (through a Boat after a 3-of-a-kind). Fixed in the client (glow stops at the last winning cell);
  backend added fixed-grid tests (A-A-S-A-A, S-A-A-A, A-A-A-S-A, W-S-A-A-A) + invariant "no line win contains a
  Scatter".
- Frontend `a480b50`: `Home.NetCast.cs` + `NetCastFx` (net sweep, splash, NET CAST!/BIG NET! banner, light shake on
  Big), cells pop into fish one by one, then the normal win flow. Skippable, reduced-motion aware.
- Merged `24f3293`. Coordinator verified: build clean, 101/101 tests, sim 2M (1 in 50.1, RTP 94.98%), real net cast
  from the live server via API (Small, 1 Wild, 0 → 3 fish, collect 9.00), page loads without errors. Visual feel
  of the net animation still needs a human look (agents' browser pane was hidden).

## Where we left off
- Next (user decides): Phase 3 sound, Phase 2 excitement moments (scatter tease, fish values flying to the
  fisherman, free-spins mood, multiplier ladder); tester pass using the T1 seams; remaining frontend items
  H1 (publish), C1, C3, C4, C8; Q1–Q3. Title icon is still an emoji. Backend worktree `..\LuckyLure-backend`
  can be reused for the next backend branch.
  After T1: tester adds the blocked deterministic tests.
- Later: SVG symbol art, persistent sessions (database), deploy.

## .NET briefs given to the user
1. SDK/CLI, csproj, Web SDK, LTS, nullable, implicit usings, API-contract-first.
2. Minimal hosting, DI lifetimes, middleware pipeline, minimal APIs, records, thread safety, decimal, secure RNG.
3. System.Text.Json, cookies (HttpOnly/SameSite/Secure), server-authoritative design, fetch/status codes.
4. Solutions/projects, shared class library, Blazor WASM, Razor components, async/await instant spin, client DI.
5. Build pipeline (Roslyn, IL, JIT), reading compiler errors (CS0246), project reference vs `using`, zero warnings.
6. Code-behind, CSS isolation, JSON source generation, trimming, JS interop, IDisposable, Directory.Build.props.
7. Git basics: init, .gitignore, staging, commits, remotes, push, Credential Manager.
8. Hosting Blazor WASM: SPA fallback, routing order, MapStaticAssets fingerprinting, environments, build/run/publish.
9. Code review lessons: never trust the client, scoped middleware, testability seams, async discipline, RTP.
10. Monte-Carlo simulation, reusing business logic, multi-threading, Release vs Debug, variance.
11. Testing: xUnit Fact/Theory, invariant testing, WebApplicationFactory + IClassFixture, traits, Skip as to-do.
12. Route groups, endpoint filters vs middleware, rate limiting (token bucket, 429), route constraints, 404 vs 405.
13. Contract-first parallel work, pattern matching (`is { } x`), collection expressions, record equality in tests.
14. DI seams / Dependency Inversion, TimeProvider + FakeTimeProvider, InternalsVisibleTo, global exception
    handling, forwarded headers, HSTS. (Plus git worktrees and merging.)
15. Delegates as strategy seams, decide-then-apply (NetPlan/CastNet), partial Fisher–Yates sampling,
    Blazor `@key` diffing and in-place updates.
