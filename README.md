# Stockroom: a sneaker store run by Aspire

A small demo store with three parts, all started by one `aspire run`:

- `web/` is a Vue 3 + Vite frontend.
- `SneakerStore.Api/` is a C# minimal API.
- MongoDB runs in a container. The API seeds it with 12 sneakers on first start.

`apphost.cs` is a single-file Aspire AppHost that wires them together.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- [Aspire CLI](https://aspire.dev/get-started/install-cli/) 13.6 or newer
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

## How the pieces connect

| Piece | How it gets its connection info |
| --- | --- |
| API to MongoDB | `WithReference(sneakerDb)` in the AppHost gives the API a `ConnectionStrings__sneakerdb` variable. `builder.AddMongoDBClient("sneakerdb")` reads it. |
| Vite to API | `WithReference(api)` gives the Vite process an `API_HTTP` variable. `vite.config.ts` proxies `/api` to it. |

## API

| Route | Returns |
| --- | --- |
| `GET /api/sneakers?brand=Nike` | Sneakers, optionally filtered by brand |
| `GET /api/sneakers/{id}` | One sneaker |
| `GET /api/brands` | Distinct brand names |
| `GET /health` | Health checks, including MongoDB (Development only) |

Brand and model names in the seed data are used only to describe the shoes. The illustrations are drawn in SVG, and there are no product photos.
