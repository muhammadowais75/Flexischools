# Flexischools Ordering API

A minimal ordering & payments API slice for the Flexischools canteen platform, built with .NET 8, EF Core, MediatR, and SQLite.

---

## How to Run Locally

### Prerequisites
- .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Start the API

```bash
cd src/Flexischools.Api
dotnet run
```

Swagger UI is served at: **http://localhost:5000** (root)

On startup the API will:
1. Create the SQLite database (`flexischools.db`)
2. Seed one Parent, one Student, one Canteen, and four MenuItems
3. Print all seeded IDs to the console — copy these into Swagger

### Run All Tests

```bash
cd src/Flexischools.Tests
dotnet test --logger "console;verbosity=normal"
```

---

## Project Structure

```
src/
├── Flexischools.Api/
│   ├── Controllers/          # Thin HTTP layer — delegates to MediatR
│   ├── Domain/
│   │   ├── Entities/         # Parent, Student, Canteen, MenuItem, Order
│   │   ├── Enums/            # OrderStatus
│   │   └── Exceptions/       # Typed domain exceptions
│   ├── Application/
│   │   ├── Orders/
│   │   │   ├── Commands/     # CreateOrderCommand + Handler
│   │   │   └── Queries/      # GetOrderQuery + Handler
│   │   └── Common/           # ExceptionHandlingMiddleware
│   └── Infrastructure/
│       ├── Persistence/      # AppDbContext, EF configs, DatabaseSeeder
│       └── Idempotency/      # IdempotencyRecord, IdempotencyService
└── Flexischools.Tests/
    ├── Unit/Domain/          # Pure domain logic tests (no DB, no HTTP)
    └── Integration/          # Full HTTP → handler → SQLite tests
```

---

## Design & Architecture Decisions

### 1. CQRS with MediatR
Commands (state-changing) and Queries (read-only) are separated into distinct handler classes. Controllers are intentionally thin — they translate HTTP concerns (headers, status codes) into MediatR messages and back. This separation makes the business logic independently testable without spinning up an HTTP server.

### 2. Rich Domain Model
All five business rules live inside `Order.Create(...)`, not in the handler or controller:
- Cut-off validation → `Canteen.IsOrderAllowed()`
- Allergen check → `Student.HasAllergenConflict()`
- Stock check → `MenuItem.TryDeductStock()`
- Wallet debit → `Parent.DebitWallet()`

Rules are evaluated in deliberate order: cheapest (no DB) first, side effects last. This means if the wallet is short, no stock has been touched yet.

### 3. Idempotency
The idempotency record is written **inside the same database transaction** as the order row. This guarantees atomicity: the order and its idempotency key either both persist or both roll back. TTL is 24 hours, enforced by comparing `ExpiresAtUtc` at read time.

### 4. Transactions & Optimistic Concurrency
`CreateOrderCommandHandler` wraps the full operation (load → validate → write) in an explicit `BeginTransactionAsync`. `Parent` and `MenuItem` both carry a `RowVersion` concurrency token. Under contention (two requests deducting the same wallet or stock simultaneously), EF Core will throw `DbUpdateConcurrencyException` on the second writer — the caller can retry. This prevents overselling without pessimistic locking.

### 5. Time Zone Handling
Cut-off times are evaluated in **Australia/Sydney (AEST/AEDT)** using `TimeZoneInfo.FindSystemTimeZoneById`. The `TimeProvider` abstraction is injected, so tests can substitute a fixed clock without any mocking framework gymnastics.

### 6. Error Handling
`ExceptionHandlingMiddleware` converts all domain exceptions to **RFC 7807 Problem Details** responses. No try/catch in controllers. Each exception maps to a specific HTTP status:
- `NotFoundException` → 404
- `OrderCutOffException`, `InsufficientStockException`, `InsufficientWalletBalanceException`, `AllergenConflictException` → 422

---

## Trade-offs & What I'd Do Next

| Area | Current | With More Time |
|---|---|---|
| **Auth** | Stubbed (no auth) | JWT Bearer; ParentId from claims; endpoint policy: parent can only order for own students |
| **Migrations** | `EnsureCreated()` | Proper EF migrations; seeder as a migration |
| **Stock reset** | Manual/seed only | Scheduled `IHostedService` resetting daily stock at midnight per canteen TZ |
| **Outbox** | Not implemented | Transactional outbox table + background poller emitting `OrderConfirmed` to a message bus |
| **Concurrency retries** | None | Polly retry policy on `DbUpdateConcurrencyException` in handler |
| **Pagination** | N/A | `GET /orders?parentId=&page=&size=` |
| **Cancellation** | Domain method exists | `PATCH /orders/{id}/cancel` endpoint + wallet refund |
| **Rate limiting** | None | ASP.NET Core rate limiting middleware on POST /orders |

---

## Part 2 — Production Architecture Sketch

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Clients (App / Web)                           │
└───────────────────────────────┬──────────────────────────────────────┘
                                │ HTTPS / JWT
                    ┌───────────▼───────────┐
                    │     API Gateway / BFF  │
                    │  • Auth (JWT verify)   │
                    │  • Rate limiting       │
                    │  • Correlation ID      │
                    │  • TLS termination     │
                    └───────────┬───────────┘
                                │
                    ┌───────────▼───────────────────────────┐
                    │        Ordering Microservice           │
                    │         (.NET 8 / MediatR)             │
                    │                                        │
                    │  POST /orders                          │
                    │    ├─ Idempotency check (DB)           │
                    │    ├─ Cut-off check (AEST/AEDT)        │
                    │    ├─ Allergen check                   │
                    │    ├─ Stock check (optimistic lock)    │
                    │    ├─ Wallet debit (optimistic lock)   │
                    │    └─ Outbox: OrderConfirmed event     │
                    │                                        │
                    │  GET /orders/{id}                      │
                    │  GET /health                           │
                    └──────┬────────────────┬───────────────┘
                           │                │
               ┌───────────▼───┐    ┌───────▼───────────┐
               │  PostgreSQL   │    │  Outbox Poller     │
               │  + EF Core    │    │  (BackgroundSvc)   │
               │               │    └───────┬───────────┘
               │  Tables:      │            │ publish
               │  • orders     │    ┌───────▼───────────┐
               │  • parents    │    │  RabbitMQ /        │
               │  • students   │    │  Azure Service Bus │
               │  • menu_items │    └───────┬───────────┘
               │  • idempotency│            │ subscribe
               └───────────────┘    ┌───────▼───────────┐
                                    │  POS / Canteen     │
                                    │  Integration Svc   │
                                    └───────────────────┘
               ┌───────────────────────────────────────────┐
               │  Observability                             │
               │  • Structured logs → Seq / Datadog         │
               │  • Metrics → Prometheus / Grafana          │
               │  • Traces → OpenTelemetry / Jaeger         │
               └───────────────────────────────────────────┘
```

### Key Production Concerns

**Idempotency**: Unique DB index on `idempotency_records.key`. The record is written in the same transaction as the order row — no window for partial state.

**Time zones**: Cut-off times stored as `TimeSpan` (local offset). All comparisons done after converting UTC now → `Australia/Sydney` using `TimeZoneInfo` (handles AEST/AEDT DST automatically).

**Concurrency / overselling**: Optimistic concurrency tokens (`RowVersion`) on both `Parent` (wallet) and `MenuItem` (stock). Under contention, EF throws; a Polly retry policy in the handler retries the full load-validate-write cycle.

**Rollbacks / refunds**: `CancelOrder` command credits wallet and restores stock in a single transaction. A background job sweeps orders stuck in `Placed` past the fulfilment date and cancels + refunds them.

**Outbox pattern**: `OrderConfirmed` event written to an `outbox_messages` table in the same transaction. A hosted `OutboxDispatcherService` polls every few seconds, publishes to the bus, and marks messages as sent. Guarantees at-least-once delivery to downstream POS systems.
