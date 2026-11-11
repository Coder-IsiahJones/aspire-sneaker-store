# Stockroom: a sneaker store run by Aspire

A small demo store with four parts, all started by one `aspire run`:

- `web/` is a Vue 3 + Vite frontend.
- `SneakerStore.Api/` is a C# minimal API.
- MongoDB runs in a container. The API seeds it with 12 sneakers on first start.
- Redis runs in a container. It holds shoppers' bags and caches catalog responses.

`apphost.cs` is a single-file Aspire AppHost that wires them together.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- [Aspire CLI](https://aspire.dev/get-started/install-cli/) 17.1 from the daily channel. Aspire 17 isn't on nuget.org yet, so the repo pins a 17.1 preview build and `nuget.config` adds the feed it comes from. If you already have the CLI, switch it with `aspire update --self --channel daily`.
- Docker (Docker Desktop, or Docker Engine on Linux)
- Node.js 24 (`nvm use` picks it up from `.nvmrc`)

## Run it

```bash
nvm use
aspire run
```

The CLI prints a link to the Aspire dashboard. The `web` resource's URL opens the store.

The first run pulls the MongoDB image and installs the npm packages, so it takes a minute. The data lives in a Docker volume, so it survives restarts. To start over with a fresh catalog, delete the volume:

```bash
docker volume ls | grep mongo-data
docker volume rm <volume-name>
```

### From VS Code

Install the recommended extensions (the Aspire extension and C# Dev Kit), then press F5 and pick **Aspire: Run sneaker store**. The debugger attaches to the API, so breakpoints in `SneakerStore.Api` work. The **(http)** variant serves the dashboard over plain HTTP, which avoids certificate warnings when the dev certificate isn't trusted.

VS Code starts the AppHost with its own environment, not your terminal's, so `dotnet`, `aspire`, `docker`, and Node 24 all have to be on its `PATH`. On WSL, VS Code's server doesn't run `.bashrc`. Put the setup in `~/.vscode-server/server-env-setup` instead:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$HOME/.aspire/bin:$PATH"
export NVM_DIR="$HOME/.nvm"
. "$NVM_DIR/nvm.sh"
nvm use 24 >/dev/null
```

Then restart the server with `wsl --shutdown` from Windows and reopen the folder.

## Tests

```bash
dotnet test --project SneakerStore.Api.Tests   # API, needs Docker
cd web && npm test                             # frontend
```

GitHub Actions runs the frontend tests and production build alongside the API
tests on pull requests and pushes to `main`. The API tests use Docker for their
MongoDB test container. The workflow can also be started manually from the
Actions tab. The API job temporarily uses `ubuntu-22.04` because newer runner
kernels are incompatible with MongoDB 8.0. GitHub plans to retire that runner
image in April 2027, so this workaround will need to be revisited.

- `SneakerStore.Api.Tests/` uses xUnit v3. The endpoint and seeder tests start throwaway `mongo:8.0` and `redis:8.0` containers with Testcontainers and run the API in memory against them, so the sorting and brand filtering are checked against real MongoDB collation. `global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10+ SDK.
- `web/` uses Vitest with Vue Test Utils and happy-dom. Tests sit next to the code they cover (`*.test.ts`) and mock `fetch` or the `api` module, so they don't need the API running. `npm run test:watch` reruns them on save.

## How the pieces connect

| Piece | How it gets its connection info |
| --- | --- |
| API to MongoDB | `WithReference(sneakerDb)` in the AppHost gives the API a `ConnectionStrings__sneakerdb` variable. `builder.AddMongoDBClient("sneakerdb")` reads it. |
| API to Redis | `WithReference(redis)` in the AppHost gives the API a `ConnectionStrings__redis` variable. `builder.AddRedisOutputCache("redis")` reads it. |
| Vite to API | `WithReference(api)` gives the Vite process an `API_HTTP` variable. `vite.config.ts` proxies `/api` to it. |

## Redis

Each browser's bag is a Redis hash at `bag:{id}`, keyed by the signed-in account or, for anyone signed out, the same `localStorage` ID as raffle entries. The hash stores only sneaker IDs, sizes and quantities, so names and prices always come from MongoDB. A bag expires 30 days after its last add. The Redis data volume keeps bags across restarts.

`GET /api/sneakers`, `GET /api/sneakers/{id}` and `GET /api/brands` are cached in Redis for 60 seconds with ASP.NET output caching. A cache hit has an `Age` response header. The cache is cleared when the API starts, and drops and bags are never cached.

## Accounts

Shoppers can sign up with an email and password. Passwords are hashed with ASP.NET's `PasswordHasher`, and sessions use an HttpOnly, SameSite=Strict cookie that lasts 14 days. Register and sign-in are limited to 10 attempts per minute per IP address, which you can change with `Auth:AttemptsPerMinute`.

Signing in moves the browser's bag and raffle entries, including wins, to the account. An account's bag, saved shoes and orders then follow it to any device. Signing out leaves none of that on the device.

Checkout needs an account and turns the bag into an order. It's a demo, so it takes no payment or shipping address and doesn't reduce stock. There is no email verification or password reset.

## Drops

The loading dock at the top of the store runs raffles for limited pairs that aren't in the regular catalog. A drop counts down to opening, takes entries until it closes, then draws winners at random. The draw runs the first time the drop is read after closing, and the first result written is permanent. The API only lets a winner add their pair to the bag, in the size they entered, one at a time.

A signed-in shopper gets one entry per drop across all their devices. Anyone signed out is a random ID kept in the browser's `localStorage`, so clearing storage or switching browsers gets a new ID.

The seed schedule is relative to when it was created: one drop is live for 10 minutes, one opens 2 minutes in, and one opens in 2 days. The live drop includes placeholder entries, so it can be lost as well as won. When the API starts and no drop is live, it deletes all drops and entries and creates a fresh schedule.

## API

| Route | Returns |
| --- | --- |
| `GET /api/sneakers?brand=Nike` | Sneakers, optionally filtered by brand |
| `GET /api/sneakers/{id}` | One sneaker |
| `GET /api/brands` | Distinct brand names |
| `GET /api/bag` | The bag for the `X-Entrant-Id` header |
| `POST /api/bag/items` | Add `{ "sneakerId": "...", "size": 9.5 }` to the bag and return it. Returns 403 for a limited pair the caller didn't win. |
| `DELETE /api/bag/items/{key}` | Remove a line and return the bag |
| `GET /api/account` | The signed-in account, or `{ "account": null }` |
| `POST /api/account/register` | Create an account from `{ "email": "...", "password": "..." }` and sign in |
| `POST /api/account/sign-in` | Sign in with `{ "email": "...", "password": "..." }` |
| `POST /api/account/sign-out` | Sign out |
| `GET /api/wishlist` | Saved sneakers, newest first. Requires sign-in. |
| `PUT /api/wishlist/{sneakerId}` | Save a sneaker. Requires sign-in. |
| `DELETE /api/wishlist/{sneakerId}` | Remove a saved sneaker. Requires sign-in. |
| `GET /api/orders` | The account's orders, newest first. Requires sign-in. |
| `POST /api/orders` | Turn the bag into an order. Requires sign-in. Returns 409 if the bag is empty. |
| `GET /api/drops` | Raffle drops and the server's clock. Send `X-Entrant-Id` to include that entrant's entry and result. |
| `POST /api/drops/{id}/entries` | Enter a drop's raffle with `{ "size": 9.5 }` and an `X-Entrant-Id` header. Returns 409 if the drop isn't open or that entrant already entered. |
| `GET /health` | Health checks, including MongoDB (Development only) |
| `GET /swagger` | Swagger UI (Development only). The dashboard links to it from the `api` resource. |
| `GET /openapi/v1.json` | OpenAPI document (Development only) |

Brand and model names in the seed data are used only to describe the shoes. The illustrations are drawn in SVG, and there are no product photos.
