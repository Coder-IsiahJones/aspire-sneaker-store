# Stockroom: a sneaker store run by Aspire

A small demo store, all started by one `aspire run`:

- `web/` is a Vue 3 + Vite frontend.
- `SneakerStore.Api/` is a C# minimal API.
- `SneakerStore.Worker/` is a C# worker that sends receipts and packs orders. Two copies run.
- `ops/` is a React + Vite dashboard for watching the queues.
- MongoDB runs in a container. The API seeds it with 12 sneakers on first start.
- Redis runs in a container. It holds shoppers' bags and caches catalog responses.
- RabbitMQ runs in a container. Checkout announces each order through it.

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

The CLI prints a link to the Aspire dashboard. The `web` resource's URL opens the store, and the `ops` resource's URL opens the back-room dashboard.

The first run pulls the container images and installs the npm packages, so it takes a minute. The data lives in a Docker volume, so it survives restarts. To start over with a fresh catalog, delete the volume:

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
dotnet test --project SneakerStore.Api.Tests   # API and worker, needs Docker
cd web && npm test                             # Vue store
cd ops && npm test                             # React dashboard
```

GitHub Actions runs both frontends' tests and production builds alongside the API
tests on pull requests and pushes to `main`. The API tests use Docker for their
MongoDB, Redis and RabbitMQ test containers. The workflow can also be started manually from the
Actions tab. The API job temporarily uses `ubuntu-22.04` because newer runner
kernels are incompatible with MongoDB 8.0. GitHub plans to retire that runner
image in April 2027, so this workaround will need to be revisited.

- `SneakerStore.Api.Tests/` uses xUnit v3. The endpoint and seeder tests start throwaway `mongo:8.0`, `redis:8.0` and `rabbitmq:4.3-management` containers with Testcontainers and run the API in memory against them, so the sorting and brand filtering are checked against real MongoDB collation, and published orders go through a real broker. The worker's handlers are tested in the same project. `BrokerSemanticsTests` doesn't test Stockroom at all: it records how RabbitMQ treats a message that keeps failing, because the worker's error handling depends on it. `global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10+ SDK.
- `web/` uses Vitest with Vue Test Utils and happy-dom. Tests sit next to the code they cover (`*.test.ts`) and mock `fetch` or the `api` module, so they don't need the API running. `npm run test:watch` reruns them on save.
- `ops/` uses Vitest with React Testing Library and happy-dom, set up the same way.

## How the pieces connect

| Piece | How it gets its connection info |
| --- | --- |
| API to MongoDB | `WithReference(sneakerDb)` in the AppHost gives the API a `ConnectionStrings__sneakerdb` variable. `builder.AddMongoDBClient("sneakerdb")` reads it. |
| API to Redis | `WithReference(redis)` in the AppHost gives the API a `ConnectionStrings__redis` variable. `builder.AddRedisOutputCache("redis")` reads it. |
| API and worker to RabbitMQ | `WithReference(messaging)` gives each a `ConnectionStrings__messaging` variable. `builder.AddRabbitMQClient("messaging")` reads it and registers one shared `IConnection`. |
| Worker to MongoDB | The same `WithReference(sneakerDb)` as the API. The worker only writes two timestamps on an order. |
| Vite to API | `WithReference(api)` gives each Vite process (`web` and `ops`) an `API_HTTP` variable. Its `vite.config.ts` proxies `/api` to it. |

## Redis

Each browser's bag is a Redis hash at `bag:{id}`, keyed by the signed-in account or, for anyone signed out, the same `localStorage` ID as raffle entries. The hash stores only sneaker IDs, sizes and quantities, so names and prices always come from MongoDB. A bag expires 30 days after its last add. The Redis data volume keeps bags across restarts.

`GET /api/sneakers`, `GET /api/sneakers/{id}` and `GET /api/brands` are cached in Redis for 60 seconds with ASP.NET output caching. A cache hit has an `Age` response header. The cache is cleared when the API starts, and drops and bags are never cached.

## Accounts

Shoppers can sign up with an email and password. Passwords are hashed with ASP.NET's `PasswordHasher`, and sessions use an HttpOnly, SameSite=Strict cookie that lasts 14 days. Register and sign-in are limited to 10 attempts per minute per IP address, which you can change with `Auth:AttemptsPerMinute`.

Signing in moves the browser's bag and raffle entries, including wins, to the account. An account's bag, saved shoes and orders then follow it to any device. Signing out leaves none of that on the device.

Checkout needs an account and turns the bag into an order. It's a demo, so it takes no payment or shipping address and doesn't reduce stock. There is no email verification or password reset.

## Messaging

Checkout saves the order, publishes one message and returns. What follows from an order happens in the worker:

1. The API publishes `order.placed` to the `stockroom.orders` topic exchange.
2. Two queues are bound to that key, so each gets its own copy. `ReceiptWorker` takes `receipts` and "sends" an email. `WarehouseWorker` takes `warehouse` and "packs" the order, which takes two seconds. Neither does anything real.
3. Each job writes a timestamp on the order (`ReceiptSentAt`, `PackedAt`) and publishes `order.confirmed` or `order.packed`.
4. The API listens to `order.#` on a queue of its own and relays the signed-in shopper's events to the browser over Server-Sent Events. The receipt in the store ticks off "Receipt sent" and "Packed" without a refresh.

| Queue | Bound to | Consumed by | Notes |
| --- | --- | --- | --- |
| `receipts` | `order.placed` | `ReceiptWorker` | Quorum queue, manual acks, prefetch 1 |
| `warehouse` | `order.placed` | `WarehouseWorker` | Quorum queue, manual acks, prefetch 1 |
| `dead-letters` | `stockroom.dead` (fanout) | Nobody | Where a message goes when its queue gives up on it |
| unnamed | `order.#` | The API's live feed | Exclusive and auto-delete, one per API instance, auto-ack |

All the names live in `SneakerStore.Messaging/Topology.cs`, which the API, the worker and the tests share.

**When a job fails.** The worker rejects the message with requeue, and the broker delivers it again. Both work queues set `x-delivery-limit` to 3, so a message that keeps failing is delivered four times (once, then three more) and then moved to `dead-letters`. The worker uses `BasicRejectAsync` and not `BasicNackAsync` for this: on RabbitMQ 4.3 a nack with requeue doesn't count toward the limit, so the message would come back forever. A worker that dies while holding a message counts the same as a reject. `BrokerSemanticsTests` checks all of this against a real broker.

**Delivery is at-least-once.** If a worker dies after doing a job and before acknowledging it, the order is delivered again. The timestamps are only written while they are still empty, so a repeat changes nothing in the database. The simulated email would go out twice.

**What's missing.** There is no outbox. If RabbitMQ is unreachable at checkout, the order is saved, the API logs an error, and nobody is told: that order never gets a receipt or a packing time. Retries are immediate, with no backoff. Orders placed before this feature existed never get their ticks.

### Watch it

- **Back room** (`ops` in the dashboard): how many messages wait in each queue, how many workers are on it, and every event as it arrives. "Start the rush" publishes made-up orders, as if a drop just opened. Tick the box and one of them goes to an address the simulated mail provider always refuses.
- **RabbitMQ's management UI** (the `Manage` link on the `messaging` resource): the user is `guest`, and the password is the `RABBITMQ_DEFAULT_PASS` environment variable shown on that resource. This is where you can read what landed in `dead-letters`.
- **Traces**: open the trace for a `POST /api/orders`. The publish, both workers' deliveries and their database writes are spans in the same trace.

### Things to try

- Start a rush of 25. `receipts` empties in a few seconds. `warehouse` takes about 25, because two workers pack one order every two seconds each.
- Stop both `worker` replicas in the dashboard, then start a rush. The orders wait in the queues. Start the workers and they drain. From the CLI, each replica has its own name: `aspire describe` lists them, and `aspire resource <name> stop` stops one.
- Stop one replica mid-rush. The other keeps going at half the speed.
- Stop `messaging`, place an order in the store, and start it again. The order is saved and its receipt stays at "on its way": the gap an outbox would close. The API and the workers reconnect by themselves.

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
| `POST /api/orders` | Turn the bag into an order and announce it. Requires sign-in. Returns 409 if the bag is empty. |
| `GET /api/orders/events` | A Server-Sent Events stream for the account: `ready` once, then `order.confirmed` and `order.packed` with `{ "orderId": "...", "at": "..." }`. Requires sign-in. |
| `GET /api/ops/queues` | Messages waiting and consumer count for each queue (Development only) |
| `GET /api/ops/events` | A Server-Sent Events stream of every order event, named by routing key (Development only) |
| `POST /api/ops/drop-rush?orders=25&doomed=true` | Publish made-up orders. With `doomed`, one can never get its receipt (Development only) |
| `GET /api/drops` | Raffle drops and the server's clock. Send `X-Entrant-Id` to include that entrant's entry and result. |
| `POST /api/drops/{id}/entries` | Enter a drop's raffle with `{ "size": 9.5 }` and an `X-Entrant-Id` header. Returns 409 if the drop isn't open or that entrant already entered. |
| `GET /health` | Health checks, including MongoDB (Development only) |
| `GET /swagger` | Swagger UI (Development only). The dashboard links to it from the `api` resource. |
| `GET /openapi/v1.json` | OpenAPI document (Development only) |

Brand and model names in the seed data are used only to describe the shoes. The illustrations are drawn in SVG, and there are no product photos.
