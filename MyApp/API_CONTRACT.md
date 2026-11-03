# Lucky Lure — API contract

Shared agreement between the backend (ASP.NET Core, `Program.cs` + `Game/`) and the
frontend (`MyApp.Client`, Blazor WebAssembly hosted by the server). Neither side changes this file on its own; propose changes instead.

Original fishing-themed slot. Play money only. No third-party slot names, logos or assets.

## Conventions

- JSON, camelCase property names, enums serialized as **strings**.
- Money values are JSON numbers (C# `decimal`), rounded to 2 decimals.
- Player identity is an HttpOnly cookie (`lucky_lure_sid`) set by the server on first request.
  The frontend just uses same-origin `fetch`; it never reads or sends the id itself.
- Errors: HTTP 400 with `{ "error": "message" }`.

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
  "state": { ...GameState after this spin }
}
```
Errors: invalid bet, insufficient balance → 400.

### `POST /api/reset` → `GameState`
Resets balance to 1000 and clears any free-spin feature.
