# AwadyLab.Mediator SampleApp

A two-service, hands-on demonstration of `AwadyLab.Mediator`'s three notification delivery modes, notification pipeline behaviors, performance metrics & observability, and two broker transports, working across real process boundaries — not just isolated snippets.

| | Service A (`:5001`) | Service B (`:5002`) |
| :--- | :--- | :--- |
| Direct delivery | ✅ `POST /direct` | — |
| Queue delivery (internal in-memory channel) | ✅ `POST /queue` | — |
| Queue delivery (durable Redis Stream) | — | ✅ `POST /shipments` |
| Broker delivery (RabbitMQ) | ✅ publishes, `POST /orders` | ✅ consumes, no endpoint — watch the console |
| Request/Response (`IRequestMediator`) | ✅ `POST /orders` | — |
| Notification Pipeline (`[Idempotent]`) | ✅ active on `POST /queue` (deduplication) | — |
| Observability & Performance Metrics | — | ✅ `GET /metrics`, OpenTelemetry `Meter`, retries & dead-letter tracking |

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

### Queue delivery & `[Idempotent]` Pipeline Behavior (Service A)

```bash
# 1. First call: executes normally
curl -X POST "http://localhost:5001/queue?sku=WIDGET-1"
```

The notification is enqueued to `AddMediator`'s default in-memory channel and drained by the background
`NotificationQueuePump`. Around the handler dispatch, `IdempotentNotificationPipelineBehavior` inspects
`StockReorderRequestedNotification` for `[Idempotent(nameof(Sku))]`.

Service A's console will log:
```text
info: SampleApp.ServiceA.Pipelines.IdempotentNotificationPipelineBehavior[0]
      [Pipeline:Idempotent] First time seeing notification StockReorderRequestedNotification with key 'WIDGET-1'. Dispatching to handlers.
info: SampleApp.ServiceA.Handlers.StockReorderHandler[0]
      [Queue] Stock reorder processed off the background pump for WIDGET-1: 100 requested.
```

Now try sending the exact same SKU again:
```bash
# 2. Second call with same SKU: intercepted and dropped by the pipeline
curl -X POST "http://localhost:5001/queue?sku=WIDGET-1"
```

Service A's console will show:
```text
warn: SampleApp.ServiceA.Pipelines.IdempotentNotificationPipelineBehavior[0]
      [Pipeline:Idempotent] Duplicate notification detected for StockReorderRequestedNotification with key 'WIDGET-1'. Skipping handlers.
```
Notice `StockReorderHandler` is **not executed** because the pipeline intercepted and short-circuited the duplicate message.

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

### Performance Monitoring & Live Metrics (Service B)

Service B incorporates end-to-end performance monitoring across its Redis Queue and RabbitMQ Broker consumers:
* **`PerformanceMetricsNotificationPipelineBehavior<T>`**: Measures execution duration with high-precision timestamps, active in-flight counts, and records success/failure outcomes.
* **`MetricsNotificationErrorHandler`**: Intercepts transient failure attempts (`willRetry = true`), tracking retry counts.
* **`MetricsDeadLetterSink`**: Captures messages after all retry attempts are exhausted (`IDeadLetterSink`).
* **`ServiceBMetrics`**: Thread-safe collector publishing metrics via OpenTelemetry `System.Diagnostics.Metrics.Meter` and exposing a live JSON snapshot.

#### 1. Inspect Current Metrics

```bash
curl http://localhost:5002/metrics
```

Output:
```json
{
  "summary": {
    "totalExecutions": 1,
    "successfulExecutions": 1,
    "failedExecutions": 0,
    "retryAttempts": 0,
    "deadLetteredExecutions": 0,
    "inFlightExecutions": 0,
    "successRatePercentage": 100.0,
    "averageExecutionTimeMs": 2.45,
    "minExecutionTimeMs": 2.45,
    "maxExecutionTimeMs": 2.45,
    "totalExecutionTimeMs": 2.45
  },
  "byNotification": {
    "ShipmentQueuedNotification": {
      "total": 1,
      "success": 1,
      "failed": 0,
      "retryAttempts": 0,
      "deadLettered": 0,
      "averageExecutionTimeMs": 2.45,
      "minExecutionTimeMs": 2.45,
      "maxExecutionTimeMs": 2.45,
      "lastExecutionUtc": "2026-09-13T17:20:00.1234567Z"
    }
  }
}
```

#### 2. Trigger Simulated Failures & Retries

Simulate a failing handler to observe automatic retries, backoff, and dead-letter sink routing:

```bash
curl -X POST "http://localhost:5002/shipments?simulateFailure=true"
```

In Service B's console, observe the initial failure, the retries via `MetricsNotificationErrorHandler`, and finally dead-letter sink routing:
```text
warn: SampleApp.ServiceB.Handlers.ShipmentQueuedHandler[0]
      [Redis Queue] Simulating handler failure for shipment of order ...
warn: SampleApp.ServiceB.Metrics.MetricsNotificationErrorHandler[0]
      [Metrics:Retry] Attempt #1 for ShipmentQueuedNotification failed: ... Will retry according to backoff policy.
warn: SampleApp.ServiceB.Metrics.MetricsNotificationErrorHandler[0]
      [Metrics:Retry] Attempt #2 for ShipmentQueuedNotification failed: ... Will retry according to backoff policy.
error: SampleApp.ServiceB.Metrics.MetricsNotificationErrorHandler[0]
      [Metrics:RetryExhausted] Attempt #3 for ShipmentQueuedNotification failed: ... Retries exhausted; transferring to dead-letter sink.
error: SampleApp.ServiceB.Metrics.MetricsDeadLetterSink[0]
      [Metrics:DeadLetter] Notification ShipmentQueuedNotification permanently failed after 3 attempts. Routed to Dead-Letter Sink.
```

Now re-query `/metrics`:
```bash
curl http://localhost:5002/metrics
```

Notice that `failedExecutions`, `retryAttempts`, and `deadLetteredExecutions` reflect the retry attempts and failure rate.

#### 3. Reset Metrics

```bash
curl -X POST http://localhost:5002/metrics/reset
```

## Project layout

```
SampleApp/
  docker-compose.yml           # RabbitMQ + Redis, infra only — the .NET services run via `dotnet run`
  SampleApp.Contracts/         # OrderPlacedNotification + its RabbitMQ topology (OrderPlacedDefinition) —
                                #   the one thing both services must compile against identically
  SampleApp.ServiceA/          # Direct, internal-channel Queue, RabbitMQ Broker (publish), Request/Response, [Idempotent] Pipeline
  SampleApp.ServiceB/          # Redis Queue, RabbitMQ Broker (consume), Live Metrics & Observability
```

Everything specific to one service only (its own notification types, handlers, the request/response pair)
lives inside that service's project — only `OrderPlacedNotification` and its topology are shared, because
those are the only pieces that actually cross the process boundary.

## Stopping

```bash
docker compose down
```
