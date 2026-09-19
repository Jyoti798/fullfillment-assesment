# Fulfillment Service

Production-style ASP.NET Core 8 REST API designed for assessment purposes. It manages products, orders, and low-stock alerts using Clean Architecture, EF Core with SQLite, centralized ProblemDetails error handling, Swagger/OpenAPI, and a hosted background worker that monitors inventory.

## 1. Project Overview

The Fulfillment Service models a small inventory and order-management workflow:

- Create, update, list, deactivate, and restock products.
- Place orders while deducting stock atomically.
- Prevent overselling and invalid order status transitions.
- Raise low-stock alerts when inventory reaches a product's reorder threshold.
- Acknowledge and resolve alerts through the API.
- Expose a documented HTTP API through Swagger in Development.

The code is intentionally structured like a production service, but without extra infrastructure that would distract from the assessment requirements.

## 2. Key Features Demonstrated

- Atomic order processing with stock validation.
- Background processing using hosted services.
- Clean separation of concerns through Clean Architecture.
- Consistent error handling using ProblemDetails.
- Full test coverage with unit and integration tests.

## 3. Architecture

The solution follows Clean Architecture. Domain rules sit at the center, Application coordinates use cases, Infrastructure implements persistence/background concerns, and the API layer exposes HTTP endpoints.

```text
Fulfillment.Api
    -> Fulfillment.Application
        -> Fulfillment.Domain

Fulfillment.Infrastructure
    -> Fulfillment.Application
    -> Fulfillment.Domain
```

| Project | Responsibility |
|---|---|
| `Fulfillment.Domain` | Entities, enums, and business rules: product stock rules, price precision, order lifecycle, alert lifecycle. |
| `Fulfillment.Application` | DTOs, use-case services, repository interfaces, unit-of-work interface, and `LowStockScanner` business logic. |
| `Fulfillment.Infrastructure` | EF Core `DbContext`, Fluent API configuration, migrations, repositories, seeding, `UnitOfWork`, and `LowStockSentinel`. |
| `Fulfillment.Api` | Controllers, DI composition, Swagger, health checks, logging configuration, and global ProblemDetails handling. |
| `tests/*` | Unit and integration tests covering domain, application, infrastructure, API, Swagger, errors, concurrency, and background behavior. |

Important boundaries:

- Controllers use DTOs only and delegate business behavior to Application services.
- Domain entities do not depend on ASP.NET Core or EF Core attributes.
- Infrastructure depends inward on Application abstractions and provides the EF Core implementation.
- `UnitOfWork` is the single save/retry boundary for business use cases.

## 4. Tech Stack

| Area | Technology |
|---|---|
| Runtime | .NET 8 / ASP.NET Core 8 |
| Language | C# |
| Persistence | EF Core 8 |
| Database | SQLite |
| API documentation | Swagger / OpenAPI via Swashbuckle |
| Validation | DataAnnotations + domain validation |
| Errors | RFC 7807 ProblemDetails |
| Testing | xUnit, ASP.NET Core integration tests |

## 5. How to Run

Prerequisite: install the .NET 8 SDK. A newer runtime can run the app because `Directory.Build.props` enables roll-forward for executable projects.

1. Restore packages:

```bash
dotnet restore
```

2. Run the API:

```bash
dotnet run --project src/Fulfillment.Api
```

3. Open Swagger:

```text
http://localhost:5080/swagger
```

4. Run tests:

```bash
dotnet test
```

Development behavior:

- `appsettings.json` uses SQLite at `Data Source=fulfillment.db`.
- `Database:MigrateOnStartup` is `true`, so migrations are applied on startup.
- `appsettings.Development.json` enables sample data seeding and sets the sentinel interval to 15 seconds.
- Default sentinel interval outside Development is 60 seconds.

## 6. API Endpoints

List endpoints support `page` and `pageSize` (`page >= 1`, `pageSize` between 1 and 100). Enums are serialized as strings.

Note: Additional endpoints for product update/delete and order status transitions are included beyond the core assessment requirements to make the workflow complete.

| Method | Endpoint | Description | Main Responses |
|---|---|---|---|
| `GET` | `/api/products` | List products. Supports `search`, `lowStockOnly`, `page`, `pageSize`. | `200`, `400` |
| `POST` | `/api/products` | Create a product. SKU is normalized uppercase and unique. | `201`, `400`, `409` |
| `GET` | `/api/products/{id}` | Get one product by id. | `200`, `404` |
| `PUT` | `/api/products/{id}` | Update product details and active flag. | `200`, `400`, `404`, `409` |
| `DELETE` | `/api/products/{id}` | Soft-delete a product by deactivating it. | `204`, `404` |
| `PATCH` | `/api/products/{id}/stock` | Adjust stock by signed `delta`; stock cannot go below zero. | `200`, `400`, `404`, `409` |
| `GET` | `/api/orders` | List orders. Supports `status`, `page`, `pageSize`. | `200`, `400` |
| `POST` | `/api/orders` | Place an order and deduct stock atomically. | `201`, `400`, `409` |
| `GET` | `/api/orders/{id}` | Get one order by id. | `200`, `404` |
| `PUT` | `/api/orders/{id}/status` | Move an order through the allowed lifecycle. | `200`, `400`, `404`, `409` |
| `GET` | `/api/alerts` | List alerts. Supports `status`, `productId`, `page`, `pageSize`. | `200`, `400` |
| `GET` | `/api/alerts/{id}` | Get one alert by id. | `200`, `404` |
| `POST` | `/api/alerts/{id}/acknowledge` | Mark an open alert as acknowledged. | `200`, `404`, `409` |
| `POST` | `/api/alerts/{id}/resolve` | Resolve an open or acknowledged alert. | `200`, `404`, `409` |
| `GET` | `/health` | Health check including database connectivity. | `200` |

Example product create request:

```json
{
  "sku": "SKU-MANUAL-001",
  "name": "Manual Test Product",
  "description": "Product created during manual API testing",
  "unitPrice": 25.50,
  "stockQuantity": 10,
  "reorderThreshold": 5
}
```

Example order request:

```json
{
  "customerName": "Ada Lovelace",
  "customerEmail": "ada@example.com",
  "items": [
    {
      "productId": "00000000-0000-0000-0000-000000000000",
      "quantity": 3
    }
  ]
}
```

Example stock adjustment request:

```json
{
  "delta": 5,
  "reason": "supplier delivery"
}
```

## 7. Error Handling

All API errors are returned as `application/problem+json` using RFC 7807 ProblemDetails.

Every error response includes:

- `type`
- `title`
- `status`
- `instance`
- `traceId`
- `detail` where useful
- `errors` for validation failures

Common response meanings:

| Status | Meaning |
|---|---|
| `400` | Invalid body, query, enum, malformed JSON, or domain validation failure. |
| `404` | Resource or route not found. |
| `405` | Route exists, but HTTP method is not allowed. |
| `415` | Unsupported content type. |
| `409` | Business conflict: duplicate SKU, insufficient stock, invalid status transition, resolved alert, or exhausted concurrency retry. |
| `500` | Unexpected server error with a generic message; internals are not returned to the client. |

## 8. Order Lifecycle

Stock is deducted when an order is placed. Cancelling an order from `Pending` or `Confirmed` returns the reserved stock.

```text
Pending -> Confirmed -> Shipped -> Delivered
   |          |
   +----------+-> Cancelled
```

Rules enforced by the domain:

- Orders must contain at least one item.
- Each product can appear only once per order.
- Quantities must be positive.
- Inactive products cannot be ordered.
- Orders are all-or-nothing: if any line cannot be fulfilled, no stock is deducted.
- Order item prices are snapshotted when the order is placed.

## 9. Background Job

`LowStockSentinel` is a hosted `BackgroundService` registered by Infrastructure. It creates a scope for each scan and calls `ILowStockScanner`.

Each scan:

- Finds active products where `StockQuantity <= ReorderThreshold`.
- Creates one open low-stock alert when no unresolved alert already exists.
- Resolves unresolved alerts when stock recovers above threshold or the product is deactivated.
- Logs and continues on failures so the host is not stopped by one failed scan.

Duplicate alert protection is enforced by a filtered unique index on alerts:

```text
ProductId where Status <> 'Resolved'
```

If a manually resolved alert is still low stock, the sentinel may raise a fresh alert later. This is intentional because alerts mirror real stock state.

Configuration:

```json
"LowStockSentinel": {
  "Enabled": true,
  "IntervalSeconds": 60
}
```

## 10. Data Integrity and Concurrency

- EF Core migrations define the SQLite schema.
- `Products.Sku` has a unique index.
- Products are soft-deleted with `IsActive`.
- Product stock is protected from going below zero in domain logic and by a database check constraint.
- Order item quantity must be positive.
- Foreign keys preserve order history.
- All entities use an application-managed `Version` concurrency token.
- `UnitOfWork.ExecuteAsync` retries optimistic concurrency conflicts up to five attempts.
- EF Core commits each use case as one transaction during `SaveChangesAsync`.

Important indexes include:

- `Products(Sku)` unique lookup.
- `Products(Name)` for product listing order.
- Partial `IX_Products_LowStock` for sentinel scans.
- Partial unique `Alerts(ProductId)` for one unresolved alert per product.
- `Orders(Status, CreatedAt)` and `Orders(CreatedAt)` for order listing.

## 11. Design Decisions

- **SQLite:** keeps the project easy to run locally without external services.
- **Clean Architecture:** makes business rules testable and keeps controllers thin.
- **DTO-first API:** avoids leaking persistence entities into public contracts.
- **Fluent API mappings:** keeps EF configuration in Infrastructure rather than decorating Domain entities.
- **No CQRS/MediatR:** direct Application services are simpler and appropriate for this domain size.
- **ProblemDetails:** gives consistent error responses across MVC validation, routing, business exceptions, and unexpected errors.
- **In-process sentinel:** enough for the assessment; database uniqueness keeps duplicate alerts safe.

## 12. Trade-offs

- **No authentication/authorization:** out of scope for the brief; would be added at the API boundary.
- **SQLite over SQL Server/PostgreSQL:** simpler setup, but decimal precision is enforced in domain logic because SQLite does not enforce decimal precision.
- **Migrate-on-startup:** useful locally; in production migrations would usually run as a deployment step.
- **No API versioning/rate limiting/idempotency keys:** real production concerns, intentionally omitted to keep the assessment focused.
- **In-process background service:** simple and reliable for this service; a distributed scheduler or lock would be considered at larger scale.

## 13. Testing

The solution includes both unit and integration tests.

| Test Type | Coverage |
|---|---|
| Unit tests | Domain rules, Application services, low-stock scanner, EF persistence behavior, query plans, DI registration, unit-of-work retry behavior, and hosted service behavior. |
| Integration tests | Real API host over HTTP, controllers, validation, error contracts, Swagger, health behavior, orders, products, alerts, sentinel behavior, and concurrent ordering scenarios. |

Current suite:

```text
148 unit tests
106 integration tests
254 total tests
```

Run:

```bash
dotnet test
```

Assessment evidence worth capturing:

- Swagger page showing all endpoints.
- Successful product creation and product retrieval.
- Successful order creation and stock reduction.
- Insufficient-stock order returning `409 Conflict`.
- Low-stock alert created by the sentinel.
- Alert acknowledge and resolve flows.
- ProblemDetails error response with `traceId`.
- `dotnet test` output.

## 14. Manual Verification

The system was manually verified using Swagger and Postman:

- Created products and validated persistence.
- Placed valid orders and confirmed stock deduction.
- Attempted invalid orders with insufficient stock and verified `409 Conflict` responses.
- Triggered low-stock alerts via the background job.
- Acknowledged and resolved alerts through the API.
- Confirmed background job behavior through application logs.

## 15. AI-Assisted Development

This project was developed using AI assistance (Claude).

Approach:

- Broke the system into layers: Domain -> Application -> Infrastructure -> API.
- Iteratively prompted AI for each layer.
- Reviewed and refined generated code.

Critical review:

- Prevented overengineering and kept the design aligned with the assessment scope.
- Ensured business logic stayed out of controllers.
- Validated correctness through automated tests and manual verification.
- Corrected issues found during review, including API response consistency, Swagger metadata, and edge-case handling.

Human judgment was used to guide architecture, validate outputs, and ensure the system remained simple and aligned with requirements.

## 16. Useful Commands

Run API:

```bash
dotnet run --project src/Fulfillment.Api
```

Run all tests:

```bash
dotnet test
```

Add a migration:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Fulfillment.Infrastructure --startup-project src/Fulfillment.Api --output-dir Persistence/Migrations
```

Override configuration with environment variables:

```bash
LowStockSentinel__IntervalSeconds=5
Database__MigrateOnStartup=false
```
