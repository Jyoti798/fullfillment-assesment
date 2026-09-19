# Fulfillment Service

An ASP.NET Core 8 REST API for managing **products**, **orders** and **low-stock alerts**, with a background
**Low Stock Sentinel** that raises and resolves alerts automatically. Built with clean architecture, EF Core
(SQLite) and a full test suite.

## Quick start

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (a newer SDK also works).

```bash
dotnet run --project src/Fulfillment.Api      # http://localhost:5080, opens Swagger UI
dotnet test                                   # 248 tests
```

In `Development` the app creates `fulfillment.db` (SQLite), applies migrations, and seeds six sample products,
two of which are already low on stock, so the sentinel has something to report within seconds. Open
`http://localhost:5080/swagger` to explore, or try:

```bash
curl http://localhost:5080/api/products?lowStockOnly=true
curl http://localhost:5080/api/alerts?status=Open
```

## Architecture

```
Api  ──►  Application  ──►  Domain
 │             ▲
 └─► Infrastructure ┘        (Infrastructure implements Application's interfaces)
```

| Project | Responsibility |
|---|---|
| `Fulfillment.Domain` | Entities and the business rules that guard them: stock can't go negative, order status transitions, alert lifecycle. No dependencies. |
| `Fulfillment.Application` | Use-case services, DTOs with validation attributes, repository / unit-of-work interfaces, and `LowStockScanner` (the sentinel's logic). |
| `Fulfillment.Infrastructure` | EF Core `DbContext`, mappings, migrations, repositories, seeding, and the `LowStockSentinel` hosted service. |
| `Fulfillment.Api` | Controllers, exception-to-ProblemDetails handler, Swagger, health check, DI wiring. |

There is deliberately no CQRS, MediatR or generic repository: the domain is small, and plain services behind
interfaces keep the code easy to read and test.

## API

All list endpoints accept `page` (default 1) and `pageSize` (default 20, max 100) and return
`{ items, page, pageSize, totalCount }`. Enums are strings.

| Method | Route | Notes |
|---|---|---|
| GET | `/api/products` | `search` (name or SKU), `lowStockOnly` |
| GET | `/api/products/{id}` | |
| POST | `/api/products` | 201 + `Location`; SKU is unique (case-insensitive) |
| PUT | `/api/products/{id}` | name, description, price, threshold, `isActive` |
| DELETE | `/api/products/{id}` | soft delete (deactivate); idempotent |
| PATCH | `/api/products/{id}/stock` | `{ "delta": 20, "reason": "delivery" }`; a signed delta, so concurrent adjustments both apply; can't go below zero |
| GET | `/api/orders` | `status` filter, newest first |
| GET | `/api/orders/{id}` | |
| POST | `/api/orders` | deducts stock atomically; 201 |
| PUT | `/api/orders/{id}/status` | `{ "status": "Confirmed" }` |
| GET | `/api/alerts` | `status`, `productId` filters |
| GET | `/api/alerts/{id}` | |
| POST | `/api/alerts/{id}/acknowledge` | open alerts only |
| POST | `/api/alerts/{id}/resolve` | closes an open or acknowledged alert by hand. **Allowed even while stock is still low**: the sentinel then raises a fresh alert on its next scan, because alerts mirror real stock |
| GET | `/health` | includes a database check |

**Errors** are RFC 7807 `application/problem+json`, and every failure has the same shape whichever part of the
pipeline produced it (model validation, routing, media-type checks, business rules, unexpected errors):
`type`, `title`, `status`, `detail`, `instance` (e.g. `GET /api/products/{id}`) and `traceId`. Validation errors add
an `errors` object keyed by field. The `traceId` matches the `TraceId` in the server's log scope, so a support
engineer can go from a response to the exact log lines. One test asserts this for nine different error paths.

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Resource not found.", "status": 404,
  "detail": "Product '…' was not found.", "instance": "GET /api/products/…", "traceId": "00-48a89c7b…-00" }
```

| Status | When |
|---|---|
| 400 | Invalid body or query, unknown product in an order, duplicate product lines |
| 404 | Resource or route not found |
| 405 / 415 | Wrong HTTP verb / unsupported content type (also problem+json) |
| 409 | Insufficient stock, invalid status transition, duplicate SKU, alert already resolved, or a concurrent modification that still lost after retries |
| 500 | Unexpected error (generic message; the exception is logged and **never** returned, and a test proves nothing leaks) |

### Order lifecycle

```
Pending ──► Confirmed ──► Shipped ──► Delivered
   │            │
   └────────────┴──► Cancelled        (returns the reserved stock)
```

Stock is deducted when the order is **placed**. An order is all-or-nothing: if any line is short, nothing is
deducted and the 409 lists every short line. Order lines snapshot the unit price, so later price changes never
rewrite history.

## Low Stock Sentinel

A `BackgroundService` that runs one scan at startup and then every `LowStockSentinel:IntervalSeconds`
(60 by default, 15 in Development). Each scan runs in its own DI scope and:

1. raises an `Open` alert for every **active** product with `stock <= reorderThreshold` that has no unresolved alert;
2. resolves any unresolved alert whose product has recovered above its threshold or been deactivated.

Properties worth knowing:

- **Idempotent.** A filtered unique index (`ProductId` where `Status <> 'Resolved'`) means there can never be two
  unresolved alerts for one product, even if two instances scan at once.
- **Failure-isolated.** A failed scan is logged and retried on the next tick; it never stops the host. Losing a
  race to another instance (the unique index rejecting a duplicate) is logged as a *warning*; anything unexpected
  is an *error*.
- **Acknowledging** an alert (via the API) records that someone has seen it, but it stays active until stock recovers.
- Set `LowStockSentinel:Enabled` to `false` to run the API without it.

## Concurrency and data integrity

- Every entity has a `Version` column used as an EF concurrency token. Two requests racing for the last unit
  can't both win: the loser gets `409`, and the tests prove stock never goes negative under 15 parallel orders.
- The database enforces its own invariants as a second line of defence: `StockQuantity >= 0`, `Quantity > 0`,
  unique SKU, foreign keys (with `Restrict`, so a product referenced by an order can't be hard-deleted), and the
  one-unresolved-alert-per-product index.
- `Order` and its lines are one aggregate; products are soft-deleted so historical orders stay valid.
- **Transactions.** `UnitOfWork.SaveChangesAsync` is the single commit point, and EF runs everything pending in it
  as one database transaction: placing an order (the order, its lines, and the stock deductions) either all
  commits or none of it does. A test proves this by mixing a valid order with a rejected insert and checking that
  nothing was left behind. There is no explicit `BeginTransaction` because nothing spans more than one save; adding
  one would only hold SQLite's write lock for longer.
- **Money.** The `UnitPrice` column is `decimal(18,2)`, but SQLite stores decimals as text and does not enforce
  precision, so the domain rejects prices with more than two decimal places (HTTP 400) to keep totals in whole cents.

### Indexes

Chosen from the actual query plans (`EXPLAIN QUERY PLAN`), and locked in by `QueryPlanTests`:

| Index | Serves |
|---|---|
| `Products(Sku)` unique | SKU uniqueness and lookups |
| `Products(Name)` | The catalogue listing (ordered by name) walks the index instead of sorting every product for each page. Searching (`LIKE '%x%'`) and `lowStockOnly` listings still scan; only the sentinel's own low-stock scan has a dedicated index |
| `IX_Products_LowStock`: `StockQuantity` **partial**, `WHERE IsActive AND StockQuantity <= ReorderThreshold` | The sentinel's scan. Comparing two columns can't be seeked, so the index holds only the low-stock rows (~2% of a 20,000-product catalogue in the test) and SQLite reads just those instead of every product. |
| `Alerts(ProductId)` unique, partial `WHERE Status <> 'Resolved'` | One unresolved alert per product, and the sentinel's "all unresolved alerts" read |
| `Alerts(ProductId, CreatedAt)` | "All alerts for a product" |
| `Alerts(Status, CreatedAt)` | Alert list by status |
| `Orders(Status, CreatedAt)` | Order list filtered by status |
| `Orders(CreatedAt)` | Order list, newest first |
| `OrderItems(OrderId)`, `OrderItems(ProductId)` | Loading an order's lines; the foreign key to products |

The low-stock index's filter must stay identical to `ProductRepository.GetLowStockAsync`'s predicate: SQLite only
uses a partial index when the query's `WHERE` implies the index's. A test fails if the two drift apart.

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:Fulfillment` | `Data Source=fulfillment.db` | SQLite file |
| `Database:MigrateOnStartup` | `true` | Apply EF migrations at startup |
| `Database:SeedSampleData` | `false` (`true` in Development) | Seed sample products when the table is empty |
| `LowStockSentinel:Enabled` | `true` | Run the background scan |
| `LowStockSentinel:IntervalSeconds` | `60` | Delay between scans |

Any key can be overridden with environment variables, e.g. `LowStockSentinel__IntervalSeconds=5`.

## Tests

`dotnet test` runs 248 tests:

- **Unit tests (148)**: domain rules (stock, price precision, every order-status transition, alert lifecycle);
  services and the sentinel scanner against an in-memory SQLite database built from the real migrations;
  optimistic concurrency and the retry behaviour (races are made deterministic by committing a rival change just
  before the save), unique and foreign-key constraints, atomic rollback, audit fields; the index/query-plan guards;
  DI wiring with scope validation on; and the hosted service's scheduling and failure handling.
- **Integration tests (100)**: the real app (real middleware, EF mappings and migrations) on a temporary SQLite
  file, called over HTTP. They cover each endpoint, the whole order lifecycle, the sentinel raising and resolving
  alerts (including manual resolve), concurrent orders never overselling *and* all succeeding when stock is
  plentiful, one identical error shape across nine failure paths, the 500 path with proof nothing leaks, and
  Swagger: off outside Development, and in Development a document that describes every endpoint and its responses.

Migrations: `dotnet tool restore`, then
`dotnet ef migrations add <Name> --project src/Fulfillment.Infrastructure --startup-project src/Fulfillment.Api --output-dir Persistence/Migrations`.

## Design decisions and trade-offs

- **SQLite** keeps the project runnable with zero setup. The provider stores `decimal` as text, so money is not
  aggregated in SQL (totals are computed in memory) and precision is enforced in the domain rather than the
  database. On SQL Server or Postgres I'd use native decimals. The low-stock partial index also relies on SQLite
  (and Postgres) partial indexes; SQL Server would use a filtered index on a computed column instead.
- **`Version` is app-managed** (incremented in `SaveChanges`) because SQLite has no `rowversion`.
- **Lost concurrency races are retried, then reported.** Placing an order, changing its status, adjusting stock and
  acknowledging or resolving an alert each run as one load, change, save unit (`IUnitOfWork.ExecuteAsync`). If another
  writer commits first, the change tracker is cleared and the whole unit re-runs on fresh data, up to 5 attempts, so
  a request only fails on a *genuine* conflict (stock that really has run out, an order that really was already
  cancelled), never because someone else was a moment quicker. A request only loses when another writer has just
  committed, so with N contenders and N attempts every one gets through; a test proves it with concurrent orders.
  If retries do run out the client still gets a 409.
- **Migrate-on-startup** is convenient here. With several instances, run migrations as a separate deployment step
  (`Database:MigrateOnStartup=false`).
- **Sentinel runs in-process.** With multiple instances every one would scan; the unique index keeps that correct
  (a losing instance logs a conflict and retries next tick), but a distributed lock or a single scheduler would be
  tidier at scale.
- **No authentication.** Out of scope for the brief; it would sit in front of the controllers.
- **Swagger is enabled in `Development` only** (a test asserts it is off in any other environment).
- **Left out on purpose, as deployment concerns rather than code.** Each is a real production need but none is in the
  brief, and adding them now would be premature: HTTPS redirection and HSTS (normally terminated at the proxy or
  load balancer), CORS (depends on who the clients are), rate limiting, request-size limits, API versioning, and
  idempotency keys on `POST /api/orders` (so a client retry can't place a second order).
- **Per-request logging.** Each request logs its method, path, status and elapsed time (no headers or bodies, so no
  personal data), and the log scope's `TraceId` equals the `traceId` in any error response.

## Verification notes

Developed against the .NET 10 SDK, targeting `net8.0`. This machine had no .NET 8 runtime, so `RollForward` is set
to `LatestMajor` (in `Directory.Build.props`) and everything was run on the .NET 10 runtime. On a machine with the
.NET 8 runtime it runs there natively. The integration tests talk to real Kestrel instead of the in-memory
`TestServer` for the same reason: the net8.0 `TestServer` is incompatible with newer runtimes.
