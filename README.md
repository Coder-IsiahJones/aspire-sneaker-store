# Stockroom: a sneaker store run by Aspire

A small demo store with three parts, all started by one `aspire run`:

- `web/` is a Vue 3 + Vite frontend.
- `SneakerStore.Api/` is a C# minimal API.
- MongoDB runs in a container. The API seeds it with 12 sneakers on first start.

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

- `SneakerStore.Api.Tests/` uses xUnit v3. The endpoint and seeder tests start a throwaway `mongo:8.0` container with Testcontainers and run the API in memory against it, so the sorting and brand filtering are checked against real MongoDB collation. `global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10+ SDK.
- `web/` uses Vitest with Vue Test Utils and happy-dom. Tests sit next to the code they cover (`*.test.ts`) and mock `fetch` or the `api` module, so they don't need the API running. `npm run test:watch` reruns them on save.

## How the pieces connect

| Piece | How it gets its connection info |
| --- | --- |
| API to MongoDB | `WithReference(sneakerDb)` in the AppHost gives the API a `ConnectionStrings__sneakerdb` variable. `builder.AddMongoDBClient("sneakerdb")` reads it. |
| Vite to API | `WithReference(api)` gives the Vite process an `API_HTTP` variable. `vite.config.ts` proxies `/api` to it. |

## Drops

The loading dock at the top of the store runs raffles for limited pairs that aren't in the regular catalog. A drop counts down to opening, takes entries until it closes, then draws winners at random. The draw runs the first time the drop is read after closing, and the first result written is permanent. Winners can add their pair to the bag.

There are no accounts, so an entrant is a random ID kept in the browser's `localStorage`. The database allows one entry per ID per drop, but clearing storage or switching browsers gets a new ID. A real store would tie entries to verified accounts.

The seed schedule is relative to when it was created: one drop is live for 10 minutes, one opens 2 minutes in, and one opens in 2 days. The live drop includes placeholder entries, so it can be lost as well as won. When the API starts and no drop is live, it deletes all drops and entries and creates a fresh schedule.

## API

| Route | Returns |
| --- | --- |
| `GET /api/sneakers?brand=Nike` | Sneakers, optionally filtered by brand |
| `GET /api/sneakers/{id}` | One sneaker |
| `GET /api/brands` | Distinct brand names |
| `GET /api/drops` | Raffle drops and the server's clock. Send `X-Entrant-Id` to include that entrant's entry and result. |
| `POST /api/drops/{id}/entries` | Enter a drop's raffle with `{ "size": 9.5 }` and an `X-Entrant-Id` header. Returns 409 if the drop isn't open or that entrant already entered. |
| `GET /health` | Health checks, including MongoDB (Development only) |
| `GET /swagger` | Swagger UI (Development only). The dashboard links to it from the `api` resource. |
| `GET /openapi/v1.json` | OpenAPI document (Development only) |

Brand and model names in the seed data are used only to describe the shoes. The illustrations are drawn in SVG, and there are no product photos.
