# Lucky Lure — notes for Claude

Fishing-themed slot machine (play money only) in .NET 10: ASP.NET Core server + Blazor WebAssembly client.
Repo: https://github.com/Xpecialist/lucky-lure (branch `main`).

Read this file first, then `docs/DEVLOG.md` (history, decisions, where we left off) and
`docs/REVIEW.md` (open review findings). That should be enough context; don't re-read the whole codebase.

## Working agreement (important)

- The user is the **manager**. Claude is the **coordinator** of a team of agents:
  **backend builder**, **frontend builder**, **reviewer**, **tester**. Delegate builder/reviewer/tester work to
  agents; keep backend and frontend work in separate files so builders can run in parallel.
- **Only act on the user's commands.** Finish what was asked, report, then propose next steps and wait.
  Don't start agents, change files, commit or push unless told to.
- Commit and push after each step **only when the user says so**. Commit identity: `Xpecialist`
  (same email as existing commits), set per repo with `git config user.name/user.email`.
- The user is **learning .NET for their CV**. After each step give a short "Brief N" on the .NET concepts used
  (with file:line pointers and CV keywords). Continue the numbering from `docs/DEVLOG.md`.
- `MyApp/API_CONTRACT.md` is the binding server/client spec. Agents don't change it on their own; propose changes.
- **Contract-first:** for a feature touching both sides, the coordinator updates `API_CONTRACT.md` and
  `MyApp.Shared/Models.cs` first, then starts backend and frontend builders in parallel.
- **Parallel builders:** while the frontend works/runs the server in the main folder, give the backend its own
  git worktree (e.g. `git worktree add -b backend/<topic> ..\LuckyLure-backend HEAD`) so builds don't collide on
  locked `bin/`. Builders commit on their branch but never push or merge; the coordinator verifies, merges
  (`--no-ff`) into `main`, re-runs build + tests on the merge, then asks before pushing.
- After each step: update `docs/DEVLOG.md` (entry + brief number) and `docs/REVIEW.md` statuses before committing.

## Layout

```
MyApp.sln
MyApp/                 ASP.NET Core server: Program.cs (minimal APIs, session middleware, hosts the client)
  Game/                SlotEngine.cs (rules, weights, paytable), GameSession.cs, SessionStore.cs
  API_CONTRACT.md      binding API + game rules spec
  Properties/launchSettings.json   "http" profile, port 5080, Development
MyApp.Shared/          Models.cs: shared records (Symbol, Cell, SpinResult, GameState, GameInfo, ...)
MyApp.Client/          Blazor WebAssembly UI (Pages/Home.razor(.cs), Components/, Services/GameClient.cs, wwwroot/)
tools/LuckyLure.Sim/   Monte-Carlo RTP simulator (console app driving the real SlotEngine)
docs/                  DEVLOG.md, REVIEW.md
```

## Commands (run from repo root)

```bash
dotnet build MyApp.sln
dotnet run --project MyApp --launch-profile http          # http://localhost:5080
dotnet run -c Release --project tools/LuckyLure.Sim -- 2000000   # RTP sim, production crypto RNG, ~7 s
dotnet run -c Release --project tools/LuckyLure.Sim -- 2000000 4 fast   # Random.Shared, ~2x quicker
dotnet test MyApp.sln --filter "Category!=Slow"           # fast tests, ~5 s
dotnet test MyApp.sln                                     # incl. 1M-spin RTP test, ~13 s
```

## Gotchas

- In Claude's shells `dotnet` and `git` may not be on PATH after install: use
  `& "C:\Program Files\dotnet\dotnet.exe"` and `& "C:\Program Files\Git\cmd\git.exe"`.
- Git pushes need the user's GitHub sign-in: run the first push in the user's Terminal panel
  (Claude's own shell can't show the browser sign-in). Later pushes reuse the stored credential.
- While the server runs it locks `MyApp/bin` — stop it before building the server.
- After rebuilding the client, **restart the server** (it reads the static-asset manifest at startup;
  otherwise the page sticks on "Loading" with a 404 for `_framework/dotnet.*.js`).
- Keep the repo at a **short path** (e.g. `C:\Users\<you>\Desktop\LuckyLure`). Blazor WASM build outputs
  exceed Windows' 260-char path limit in deep folders.
- Sessions are in memory: restarting the server resets everyone to 1000 credits.
- Publish: `dotnet publish MyApp -c Release -o <folder>` works (REVIEW H1 fixed with fingerprinting off). Start the
  published `MyApp.exe` **from its own folder** (or pass `--contentRoot`), otherwise it can't find `wwwroot` and
  the page is blank. Publish outside the repo or into an ignored folder.
- Sound: Web Audio synth in `MyApp.Client/wwwroot/js/sound.js` via `Services/SoundService.cs`; nothing plays
  before the first user gesture. `window.__soundLog = []` in the console records sound calls (inert otherwise).
- Testing seams: `IRandomSource` (inject a fake RNG), `SlotEngine.Apply(...)` (internal, hand-built grids incl.
  respin grids), `TimeProvider` in `SessionStore` (use `FakeTimeProvider`). Never add debug/cheat endpoints to
  the server; force game states only in tests or with a temporary page-side stub that isn't committed.
- All API errors are `{ "error": "..." }` JSON (400/429/500). Cookie Secure, HSTS and HTTPS redirect apply only
  outside Development, so `http://localhost:5080` keeps working.
- Rate limit (per IP, token bucket 20 burst / 10 per s) applies to /state, /spin, /reset. Scripted spin loops
  need ~120 ms between calls.
- Checking the UI in the browser pane: a hidden pane throttles animations and tool screenshots can lag, so reels
  may look half a cell off mid-bounce. Judge alignment from the settled DOM (strip transform 0) and ask the user
  to judge how animations feel.
- New PC setup: install .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) and Git (`winget install Git.Git`).
