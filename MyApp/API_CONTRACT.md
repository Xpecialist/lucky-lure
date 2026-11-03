# Lucky Lure — API contract

Shared agreement between the backend (ASP.NET Core, `Program.cs` + `Game/`) and the
frontend (`MyApp.Client`, Blazor WebAssembly hosted by the server). Neither side changes this file on its own; propose changes instead.

Original fishing-themed slot. Play money only. No third-party slot names, logos or assets.

## Conventions

- JSON, camelCase property names, enums serialized as **strings**.
- Money values are JSON numbers (C# `decimal`), rounded to 2 decimals.
- Player identity is an HttpOnly cookie (`lucky_lure_sid`) set by the server on first request.
  The frontend just uses same-origin `fetch`; it never reads or sends the id itself.
  Outside Development the cookie is always `Secure` (HTTPS only, HSTS + HTTPS redirect on).
- Errors: HTTP 400 with `{ "error": "message" }`. Every error body has this shape: a malformed/unreadable
  request body is 400 `{ "error": "Invalid request body." }`, an unexpected server error is 500 with a generic
  `error` message (never details or a stack trace).
- `/api/state`, `/api/spin` and `/api/reset` are rate limited per client IP (burst 20, refills 10/s).
  Over the limit → HTTP 429 with `{ "error": "Too many requests. Slow down." }` and a `Retry-After` header.
  Only these three create the session cookie; `/api/info` never does.

## Symbols

`Ten, Jack, Queen, King, Ace, TackleBox, Dragonfly, Rod, Float, Fish, Wild, Scatter`

- `Fish` always carries a `value` (cash amount = multiplier × total bet). All other symbols have `value: null`.
- `Wild` (the fisherman) only lands during free spins. It substitutes for every symbol except `Fish` and `Scatter`.
- `Scatter` lands only in the base game, at most one per reel.

## Game rules

- 5 reels × 3 rows, 10 fixed paylines, wins pay left to right from reel 1, 3+ in a row.
- Line bet = total bet / 10. Paytable values are multiples of the line bet.
- Scatters pay anywhere: 3 / 4 / 5 → pay (× total bet) and 10 / 15 / 20 free spins.
- Fish values drawn from a weighted list: 2, 5, 10, 15, 20, 25, 50, 100 (× total bet).
- Free spins use the bet that triggered them; no balance is deducted.
- In free spins, if any `Wild` lands: collectWin = (sum of all fish values on screen) × (number of wilds) × current multiplier.
- Every 4 wilds collected (cumulative in the feature) → +10 free spins and the multiplier steps 1 → 2 → 3 → 10 (max 3 retriggers). The new multiplier applies from the next spin.
- Starting balance 1000. Bets: 0.10, 0.20, 0.50, 1, 2, 5, 10.

### Clarifications (settled after the first build)

- `Fish` also pays as a normal line symbol (runs must start on reel 1, since `Wild` can't substitute for it).
- The free-spin multiplier applies to the collect win only, not to line wins.
- One spin can cross more than one retrigger threshold; `retriggered` is then `true` once and `freeSpinsTotal` grows by 10 per retrigger.
- `freeSpinWin` sums each free spin's `totalWin` (excluding the scatter win that triggered the feature).
- After the last free spin, the feature fields in `GameState` keep their final values until the next paid spin or reset. `freeSpinsRemaining == 0` means "not in the feature".
- Paytable keys are symbol names exactly as in the enum (`"Ten"`, not `"ten"`).

### Nudge respin (base game only)

A near-miss second chance at the bonus, inside one paid spin (one bet, no extra charge).

- **Condition:** after a base-game grid is shown, if there are **exactly 2 Scatters** and **each** of them is in
  row 0 (top) or row 1 (middle), a nudge respin happens. Never in free spins.
- **Nudge:** the 2 reels holding the Scatters are **held** and shift **down one row**: row 1 → row 2, row 0 → row 1
  (Fish keep their value), and a new random base-game symbol (never a Scatter, since the reel already has one)
  enters at row 0. The other 3 reels **respin** completely with the normal base-game rules (≤ 1 Scatter per reel).
- **Repeat:** the condition is checked again on the new grid. In practice the held Scatters can only qualify again
  if both started in row 0, so there are at most **2 respins** per paid spin (the engine enforces this cap).
- **Stop:** when the condition fails. If the final grid has 3+ Scatters, scatter pay and free spins (10/15/20)
  are awarded exactly as for a normal trigger.
- **Payouts:** line wins pay on **every** grid (the original and each respin). Scatter pay / free spins are
  evaluated on the **final** grid only.
- **Balance:** the free-spin trigger rate stays about **1 in 50 paid spins overall** (respins included) and RTP
  94–97%; measured with `tools/LuckyLure.Sim`.

## Endpoints

### `GET /api/info` → `GameInfo`
```json
{
  "bets": [0.1, 0.2, 0.5, 1, 2, 5, 10],
  "paylines": [[1,1,1,1,1], ...],          // 10 lines, each = row index (0-2) per reel
  "paytable": { "Ten": [1, 5, 20], ... },   // [pay for 3, 4, 5] × line bet; includes Fish and Wild
  "scatter": { "3": { "pay": 2, "freeSpins": 10 }, "4": {...}, "5": {...} },
  "multipliers": [1, 2, 3, 10],
  "wildsPerRetrigger": 4
}
```

### `GET /api/state` → `GameState`
```json
{
  "balance": 1000,
  "freeSpinsRemaining": 0,
  "freeSpinsTotal": 0,
  "multiplier": 1,
  "wildsCollected": 0,
  "retriggers": 0,
  "freeSpinWin": 0,
  "freeSpinBet": 0
}
```

### `POST /api/spin` body `{ "bet": 1 }` → `SpinResult`
`bet` is ignored while free spins are remaining.
```json
{
  "grid": [[{ "symbol": "Fish", "value": 5 }, { "symbol": "Ace", "value": null }, ...], ...],  // grid[reel][row], 5 × 3
  "bet": 1,
  "wasFreeSpin": false,
  "lineWins": [{ "line": 0, "symbol": "Ace", "count": 3, "win": 0.3, "positions": [[0,1],[1,1],[2,1]] }],  // positions = [reel,row]
  "scatterCount": 0,
  "scatterWin": 0,
  "wildCount": 0,
  "collectWin": 0,
  "totalWin": 0.3,
  "freeSpinsAwarded": 0,     // > 0 only on the spin that triggers the feature
  "retriggered": false,
  "featureEnded": false,     // true on the last free spin
  "respins": [               // nudge respins in order; [] when none (always [] in free spins)
    {
      "grid": [[...], ...],          // full 5 × 3 grid after this respin
      "heldReels": [0, 3],           // the 2 reels that were held and shifted down one row
      "lineWins": [ ...LineWin ]     // line wins on this respin's grid
    }
  ],
  "state": { ...GameState after this spin }
}
```
- `grid` / `lineWins` at the top level are the **original** spin.
- `scatterCount`, `scatterWin`, `freeSpinsAwarded` refer to the **final** grid (last respin, or the original).
- `totalWin` = all `lineWins` (original + every respin) + `scatterWin` + `collectWin`.

Errors: invalid bet, insufficient balance → 400.

### `POST /api/reset` → `GameState`
Resets balance to 1000 and clears any free-spin feature.
