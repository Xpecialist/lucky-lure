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

## Where we left off
- Next candidates (user decides): backend batch 1 from REVIEW.md (S1 sessions/rate limit, S2 405, T1 test seams,
  S3 error bodies, S4 cookie hardening), frontend batch (H1 publish fix, C5 bet text, C1–C3/C7 client races),
  then the tester (xUnit unit tests, WebApplicationFactory integration tests, RTP test).
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
