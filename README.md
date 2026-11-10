# Stockroom: a sneaker store run by Aspire

A small demo store with three parts, all started by one `aspire run`:

- `web/` is a Vue 3 + Vite frontend.
- `SneakerStore.Api/` is a C# minimal API.
- MongoDB runs in a container. The API seeds it with 12 sneakers on first start.

`apphost.cs` is a single-file Aspire AppHost that wires them together.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
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
