┌─────────────────────────────────────────────────────┐
│                     BFF / API Gateway                │
│  (JWT auth, rate limiting, correlation ID injection) │
└───────────────┬─────────────────────────────────────┘
                │ HTTPS
┌───────────────▼──────────────────────────────────────┐
│              Ordering Microservice (.NET 8)            │
│  POST /orders ─► CreateOrderCommandHandler            │
│                   • Cut-off check (AEST timezone)     │
│                   • Allergen check                    │
│                   • Stock check (optimistic lock)     │
│                   • Wallet debit (optimistic lock)    │
│                   • Idempotency (DB table, 24h TTL)   │
│                   • Outbox event: OrderConfirmed      │
└────────┬──────────────────┬────────────────┬─────────┘
         │                  │                │
    ┌────▼────┐      ┌──────▼──────┐  ┌──────▼──────┐
    │PostgreSQL│      │Outbox Poller│  │  Seq / OTLP │
    │+ EF Core │      │(BackgroundSvc│  │  (Logging)  │
    └──────────┘      └──────┬──────┘  └─────────────┘
                             │ Publish
                      ┌──────▼──────┐
                      │  RabbitMQ/  │
                      │  Azure SB   │
                      └──────┬──────┘
                             │ Subscribe
                      ┌──────▼──────┐
                      │ POS / Canteen│
                      │  Integration│
                      └─────────────┘