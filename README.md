# Lucky Lure

A fishing-themed slot machine built end to end in .NET: an ASP.NET Core API runs the game, and a Blazor WebAssembly client renders the reels in the browser. Play money only.

## Features

- 5×3 reels, 10 paylines, wilds and scatters
- Free spins: fish symbols carry cash values, fisherman wilds collect them
- Retriggers every 4 fishermen with rising multipliers (×2, ×3, ×10)
- Server-authoritative: every outcome, balance and feature state is decided on the server using a cryptographically secure RNG
- Instant-feel spins: the reels start the moment you click and land on the server's result when it arrives

## Tech stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal APIs, dependency injection, middleware |
| Frontend | Blazor WebAssembly, Razor components, CSS isolation, JS interop |
| Shared | Class library of C# records used by both server and client |
| Serialization | System.Text.Json (source-generated on the client) |

## Project structure

```
MyApp/          ASP.NET Core server: API endpoints, slot engine, session store
MyApp.Shared/   Shared models (Symbol, Cell, SpinResult, GameState, ...)
MyApp.Client/   Blazor WebAssembly UI
MyApp/API_CONTRACT.md   API specification shared by server and client
```

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build MyApp.sln
dotnet run --project MyApp
```

> Status: the server and client both build; hosting the client from the server is the next step.

## API

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/info` | Bets, paylines, paytable, feature rules |
| GET | `/api/state` | Current balance and free-spin state |
| POST | `/api/spin` | Spin with `{ "bet": 1 }` |
| POST | `/api/reset` | Reset balance to 1000 |

See [API_CONTRACT.md](MyApp/API_CONTRACT.md) for full request and response shapes.
