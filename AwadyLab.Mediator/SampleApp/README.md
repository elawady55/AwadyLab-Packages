# AwadyLab.Mediator SampleApp

A two-service, hands-on demonstration of `AwadyLab.Mediator`'s three notification delivery modes and two
broker transports, working across real process boundaries — not just isolated snippets.

| | Service A (`:5001`) | Service B (`:5002`) |
| :--- | :--- | :--- |
| Direct delivery | ✅ `POST /direct` | — |
| Queue delivery (internal in-memory channel) | ✅ `POST /queue` | — |
| Queue delivery (durable Redis Stream) | — | ✅ `POST /shipments` |
| Broker delivery (RabbitMQ) | ✅ publishes, `POST /orders` | ✅ consumes, no endpoint — watch the console |
| Request/Response (`IRequestMediator`) | ✅ `POST /orders` | — |

Service A never has a local handler for `OrderPlacedNotification` — it's a producer-only participant in
that notification's topology. Service B never publishes anything over RabbitMQ — it only subscribes.

## Prerequisites

* .NET 10 SDK
* Docker (for RabbitMQ + Redis)

## 1. Start the infrastructure

```bash
cd AwadyLab.Mediator/SampleApp
docker compose up -d
```

This starts:
* **RabbitMQ** on `5672` (AMQP) and `15672` (management UI — `http://localhost:15672`, guest/guest). Open
  the UI after running the Broker scenario below to see the `orders.events` exchange and Service B's bound
  `service-b.orders` queue.
* **Redis** on `6379`.

Wait for both to report healthy: `docker compose ps`.

## 2. Run both services

In two separate terminals:

```bash
dotnet run --project SampleApp.ServiceA
dotnet run --project SampleApp.ServiceB
```

Each logs to its own console — keep both visible, since several scenarios below are only observable in
Service B's log after calling Service A.

## 3. Try each scenario

### Direct delivery (Service A)

```bash
curl -X POST http://localhost:5001/direct
```

`InventoryCheckedHandler` runs synchronously, in-process, before the HTTP response returns. Look for the
`[Direct]` log line in Service A's console — it appears immediately.

### Queue delivery — internal in-memory channel (Service A)

```bash
curl -X POST http://localhost:5001/queue
```

The notification is enqueued to `AddMediator`'s default in-memory channel and drained by the background
`NotificationQueuePump`. The `[Queue]` log line in Service A's console appears about a second *after* the
response returns (the handler simulates work with a delay, to make the "ran later, off the request thread"
point visible).

### Queue delivery — durable Redis Stream (Service B)

```bash
curl -X POST http://localhost:5002/shipments
```

Same `NotificationDelivery.Queue`, but Service B has `RedisOptions.Queue` configured instead of the
internal channel, so the envelope goes into a Redis Stream (`serviceb:queue`) rather than an in-memory
`Channel<T>`. Unlike Service A's `/queue` demo, this one survives a restart of Service B — stop the
process after enqueuing (before the pump drains it) and restart it; the `[Redis Queue]` log line still
appears once it comes back up.

### Broker delivery — RabbitMQ, cross-service (Service A → Service B)

```bash
curl -X POST http://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Ada Lovelace","sku":"WIDGET-1","quantity":3}'
```

This does two things in Service A:
1. `IRequestMediator.Execute(CreateOrderCommand)` — a normal request/response call, handled by
   `CreateOrderHandler`, returning an `OrderResult`.
2. Publishes `OrderPlacedNotification` with `Delivery = Broker`, over RabbitMQ, to the `orders.events`
   exchange declared by the shared `OrderPlacedDefinition` in `SampleApp.Contracts`.

Watch **Service B's** console — `OrderPlacedExternalHandler` (an `IExternalNotificationHandler<>`, bound
to its own `service-b.orders` queue) picks it up and logs `[Broker/RabbitMQ]`. There is no endpoint on
Service B for this; it fires automatically whenever Service A publishes.

## Project layout

```
SampleApp/
  docker-compose.yml           # RabbitMQ + Redis, infra only — the .NET services run via `dotnet run`
  SampleApp.Contracts/         # OrderPlacedNotification + its RabbitMQ topology (OrderPlacedDefinition) —
                                #   the one thing both services must compile against identically
  SampleApp.ServiceA/          # Direct, internal-channel Queue, RabbitMQ Broker (publish), Request/Response
  SampleApp.ServiceB/          # Redis Queue, RabbitMQ Broker (consume)
```

Everything specific to one service only (its own notification types, handlers, the request/response pair)
lives inside that service's project — only `OrderPlacedNotification` and its topology are shared, because
those are the only pieces that actually cross the process boundary.

## Stopping

```bash
docker compose down
```
