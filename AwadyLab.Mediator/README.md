# AwadyLab.Mediator

A high-performance, low-allocation Mediator, Async Streaming, and Pub/Sub Messaging engine for .NET 10+.

[![NuGet](https://img.shields.io/nuget/v/AwadyLab.Mediator.svg)](https://www.nuget.org/packages/AwadyLab.Mediator)
![Tests](https://img.shields.io/badge/tests-passing-brightgreen.svg)
![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Language](https://img.shields.io/badge/C%23-14-blue.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

`AwadyLab.Mediator` provides a clean, decoupled architecture for modern .NET applications. Dispatch is organized into three focused pillars unified by an ergonomic facade:

```mermaid
flowchart TD
    Caller(["Caller / API Endpoint"]) --> Facade{{"IMediator"}}
    
    Facade --> Req["IRequestMediator<br/>(Request / Response)"]
    Facade --> Str["IStreamMediator<br/>(Async Streaming)"]
    Facade --> Notif["INotificationPublisher<br/>(Pub / Sub)"]
    
    Req --> Pipe["Pipeline Behaviors<br/>&amp; Short-Circuit"]
    Str --> Stream["IAsyncEnumerable&lt;T&gt;<br/>&amp; Decorators"]
    Notif --> Deliv{"Delivery Mode"}
    
    Deliv --> Direct["Direct<br/>(In-Process)"]
    Deliv --> Queue["Queue<br/>(Background Channel)"]
    Deliv --> Broker["Broker<br/>(Distributed Pub/Sub)"]
    
    Pipe --> H1{{"IRequestHandler&lt;,&gt;"}}
    Stream --> H2{{"IStreamRequestHandler&lt;,&gt;"}}
    Direct --> H3{{"INotificationHandler&lt;&gt;"}}
    Queue --> H3
    Broker --> H4{{"IExternalNotificationHandler&lt;&gt;"}}
```

### Key Highlights

* **Zero-Allocation Short-Circuit**: When no pipeline behaviors are configured, requests bypass middleware overhead and invoke handlers directly without delegate closures.
* **Cached Reflection Invokers**: Eliminates dynamic `MethodInfo.Invoke` on hot dispatch paths — each (handler, request/response) or (handler, notification) pair gets a strongly-typed delegate built once at startup from a closed generic method, not a compiled expression tree (which can't legally call an internal handler type's members).
* **Automatic DI Scope Management**: Automatically reuses `HttpContext.RequestServices` when running inside web requests, and provisions isolated `IServiceScope` instances in background jobs and parallel dispatches.
* **Async Stream Processing**: Built-in support for `IAsyncEnumerable<TResponse>` with scope persistence across stream lifetimes, early request vetoing (`yield break`), and per-item response filtering.
* **Multi-Tier Notification Engine**: Supports in-process direct publishing, in-memory durable channel queues with exponential backoff retries, and cross-service broker messaging (`RabbitMQ`, `Redis`) with security sanitization.

---

## Installation

```bash
dotnet add package AwadyLab.Mediator
```

---

## Quickstart

### Register in DI (`Program.cs`)

```csharp
using AwadyLab.Mediator;

var builder = WebApplication.CreateBuilder(args);

// Register mediator scanning candidate assemblies
builder.Services.AddMediator([typeof(Program).Assembly], options =>
{
    // Optional: Add global pipeline behaviors
    options.ExecutorPipelines.Add(typeof(LoggingPipeline<,>));
    options.ExecutorPipelines.Add(typeof(ValidationPipeline<,>));
});

var app = builder.Build();
```

> [!IMPORTANT]
> `AddMediator` cannot be called more than once per `IServiceCollection`. Calling it a second time throws an `InvalidOperationException` at startup to prevent duplicate scanning, conflicting handler mappings, or duplicate pipeline registrations. Configure all options, pipelines, and transports inside that single `AddMediator(...)` invocation.

---

## 1. Request / Response (`IRequestMediator`)

Single-request, single-response execution designed for standard CQRS commands and queries.

### Defining Requests & Handlers

All response contracts are constrained to `IResponse`. `IResponse` serves as a marker interface that promotes clear architectural boundaries, guarantees consistency, and allows reflection and tooling to query and inspect all response types across your domain and assemblies. We strongly encourage adopting this pattern for all domain responses.

```csharp
using AwadyLab.Mediator.Abstraction;

// 1. Define Request and Response records — a reference type implements IResponse directly.
public record GetUserQuery(int UserId) : IRequest<UserDto>;

public record UserDto(int Id, string Name, string Email) : IResponse;

// 2. Implement Handler
public class GetUserHandler(IUserRepository repo) : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Execute(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await repo.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        return new UserDto(user.Id, user.Name, user.Email);
    }
}
```

### Value Responses (`ValueResponse<T>`)

A response type you don't own the declaration of — a primitive, an enum, or a value type from another library (e.g., `int`, `bool`, `Guid`) — cannot be given `: IResponse` directly. `ValueResponse<T>` (`T : struct`) wraps it instead, providing implicit bidirectional conversions so you can return values naturally:

```csharp
public record CountUsersQuery : IRequest<ValueResponse<int>>;

public class CountUsersHandler(IUserRepository repo) : IRequestHandler<CountUsersQuery, ValueResponse<int>>
{
    // int converts to ValueResponse<int> implicitly — no `new ValueResponse<int>(...)` needed.
    public async Task<ValueResponse<int>> Execute(CountUsersQuery request, CancellationToken cancellationToken) =>
        await repo.CountAsync(cancellationToken);
}
```

### Void-like Requests (`Unit`)

For commands that return no payload, use `Unit`:

```csharp
public record DeleteUserCommand(int UserId) : IRequest<Unit>;

public class DeleteUserHandler(IUserRepository repo) : IRequestHandler<DeleteUserCommand, Unit>
{
    public async Task<Unit> Execute(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        await repo.DeleteAsync(request.UserId, cancellationToken);
        return Unit.Value;
    }
}
```

### Pipeline Behaviors (`IPipelineBehaviorHandler<,>`)

Pipelines execute in the **exact order that they are registered** in `options.ExecutorPipelines`. For example:

```csharp
options.ExecutorPipelines.Add(typeof(LoggingPipeline<,>));    // Outer pipeline: runs first
options.ExecutorPipelines.Add(typeof(ValidationPipeline<,>)); // Inner pipeline: runs second
```

When a request is executed, it passes through `LoggingPipeline` before `next()` enters `ValidationPipeline`. On completion, the response unwinds in reverse order. Order your cross-cutting concerns deliberately (e.g., global exception handling outermost, logging second, validation third).

```csharp
public class LoggingPipeline<TRequest, TResponse>(ILogger<LoggingPipeline<TRequest, TResponse>> logger)
    : IPipelineBehaviorHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResponse
{
    public async Task<TResponse> HandleAsync(
        TRequest message,
        MessageHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
        return response;
    }
}
```

---

## 2. Asynchronous Streaming (`IStreamMediator`)

Streams large datasets, real-time events, or LLM tokens efficiently using `IAsyncEnumerable<TResponse>`.

### Defining Stream Requests & Handlers

```csharp
using System.Runtime.CompilerServices;
using AwadyLab.Mediator.Abstraction;

public record StreamMetricsRequest(int DeviceId) : IStreamRequest<MetricPoint>;

// A stream's TResponse implements IResponse too, same as a request/response call's — ValueResponse<T> is
// the escape hatch for a primitive or other struct value that can't implement IResponse itself.
public record MetricPoint(DateTime Timestamp, double Value) : IResponse;

public class StreamMetricsHandler : IStreamRequestHandler<StreamMetricsRequest, MetricPoint>
{
    public async IAsyncEnumerable<MetricPoint> ExecuteAsync(
        StreamMetricsRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken);
            yield return new(DateTime.UtcNow, Random.Shared.NextDouble());
        }
    }
}
```

### Stream Request & Response Decorators

Configure global streaming interceptors via `MediatorOptions` using pattern matching:

```csharp
builder.Services.AddMediator([typeof(Program).Assembly], options =>
{
    // Request Decorator: Vetoes or modifies request BEFORE resolving handlers or opening scopes
    options.StreamRequestDecorator = async (req, _) => req switch
    {
        StreamMetricsRequest { DeviceId: < 0 } => null, // Returning null triggers 'yield break' immediately
        _ => req
    };

    // Response Decorator: Filters or transforms individual emitted items
    options.StreamResponseDecorator = async (item, _) => item switch
    {
        MetricPoint { Value: < 0.1 } => null, // Returning null skips only this item (continue)
        _ => item
    };
});
```

* **Scope Lifetime**: The DI scope stays active for the entire duration of stream enumeration and disposes when the consumer finishes `await foreach`.

---

## 3. Pub/Sub Notifications (`INotificationPublisher`)

The notification engine distributes events to one or more subscribers using three delivery modes and configurable execution semantics:

### Delivery Modes & Execution Types

* **Delivery Modes**:
  1. **Direct (`NotificationDelivery.Direct`)**: In-process dispatch immediately executing local handlers within the current application.
  2. **Queue (`NotificationDelivery.Queue`)**: In-memory background channel queue drained asynchronously by worker loops with exponential backoff retries and dead-letter sinks.
  3. **Broker (`NotificationDelivery.Broker`)**: Distributed pub/sub across microservices (`RabbitMQ`, `Redis`) with automatic reconnect, serialization guards, and external topic routing.

* **Execution Types (Direct Mode)**:
  * **Parallel (`ExecuteParallel = true`, Default)**: All subscribed handlers execute concurrently using `Task.WhenEach`. Each handler is automatically provisioned with an **isolated `IServiceScope`**, eliminating multi-threaded race conditions on scoped dependencies like EF Core `DbContext`.
  * **Sequential (`ExecuteParallel = false`)**: Handlers execute sequentially in registration order, sharing the **Ambient Scope**, and aborting immediately if cancellation is requested.

* **What is an Ambient Scope?**:
  An **Ambient Scope** refers to the resolution scope already active in the caller's context — most commonly `HttpContext.RequestServices` during an active ASP.NET Core web request. In sequential execution, handlers share this ambient scope so they can participate in the caller's active Unit of Work (such as sharing a single EF Core `DbContext` instance or per-request caches). When running outside of an active web request (such as in a background hosted service or queue worker), the mediator provisions a dedicated scope for the dispatch and cleans it up upon completion.

* **Extensibility & Customization Interfaces**:
  You can customize or override the default behavior of the notification engine by registering your own implementations of these core interfaces in DI:

  | Interface | Default Implementation | Purpose |
  | :--- | :--- | :--- |
  | `IMessageQueue` | `ChannelMessageQueue` | Backing queue abstraction for `NotificationDelivery.Queue`. Defaults to an internal bounded `System.Threading.Channels.Channel`. |
  | `IDeadLetterSink` | `LoggingDeadLetterSink` | Receives poison envelopes that exhaust all configured retry attempts. Override to persist messages to a database, cloud storage, or alert system. |
  | `INotificationErrorHandler` | `LoggingNotificationErrorHandler` | Hook invoked on each failed dispatch attempt during retry backoff. Override to record Prometheus/OpenTelemetry metrics or emit telemetry. |
  | `IMessageSerializer` | `SystemTextJsonMessageSerializer` | Serializes and deserializes payloads for external broker transports. Override to use MessagePack, Protobuf, etc. |
  | `IMessageBroker` | None (supplied by transport packages) | Broker transport contract for `NotificationDelivery.Broker` (implemented by `AwadyLab.Mediator.RabbitMQ` and `AwadyLab.Mediator.Redis`). |

* **Configurable Defaults**:
  * The global fallback delivery mode is set by `options.DefaultNotificationDelivery` (defaults to `Direct`).
  * In-memory queue parameters (capacity, worker count, retries, jitter) are configured via `options.UseNotificationQueue(...)`.
  * External broker subscription defaults are configured via `options.UseExternalSubscriptionDefaults(...)`.
  * Any individual publish call can override options via the `Action<PublishOptions>?` configure parameter.

```csharp
public record OrderPlacedNotification(Guid OrderId, decimal Amount) : INotification;
```

> [!IMPORTANT]
> **Delivery Guarantees**: `Queue` and `Broker` delivery are **at-least-once**, not exactly-once — the standard contract for a retrying queue/broker, not a bug. If a handler's side effect (a DB write, an email, a call to another service) commits and the process then crashes before the message is acknowledged, the message **will** be redelivered and the handler **will** run again. Write handlers to be idempotent — e.g. keyed off `NotificationEnvelope.MessageId`, which is threaded end-to-end through the queue and broker transports specifically so an application can maintain its own dedup/idempotency store.

### 1. Direct Delivery (In-Process)

Dispatches immediately within the caller's process. You can select between two execution modes:

#### Parallel Mode (`ExecuteParallel = true`, Default)

```mermaid
flowchart LR
    Pub(["Publisher"]) --> H1["Handler A<br/>(Isolated Scope 1)"]
    Pub --> H2["Handler B<br/>(Isolated Scope 2)"]
    Pub --> H3["Handler C<br/>(Isolated Scope 3)"]
```

#### Sequential Mode (`ExecuteParallel = false`)

```mermaid
flowchart LR
    Pub(["Publisher"]) --> H1["Handler A"]
    H1 --> H2["Handler B"]
    H2 --> H3["Handler C"]
    
    subgraph AmbientScope ["Shared Ambient Scope"]
        H1
        H2
        H3
    end
```

```csharp
await mediator.Publish(new OrderPlacedNotification(orderId, 150m), options =>
{
    options.Delivery = NotificationDelivery.Direct;
    options.ExecuteParallel = true; // Concurrent dispatch with isolated DI scope per handler
});
```

* **Parallel Execution (`Task.WhenEach`)**: Handlers run concurrently with independent DI scopes, preventing concurrency conflicts on scoped dependencies like Entity Framework `DbContext`.
* **Sequential Execution**: Runs handlers sequentially in ambient scope, short-circuiting immediately if cancellation is requested.

### 2. Queue Delivery (Durable In-Memory Channel)

```mermaid
flowchart LR
    Pub(["Publisher"]) --> Q[("Channel Queue")]
    Q --> Pump["Worker Pump"]
    Pump --> Handlers["Handlers"]
    Handlers -. "Failure &amp; Retries Exhausted" .-> DLQ[/"Dead-Letter Sink"/]
```

Offloads notifications to an asynchronous background channel with worker loops:

```csharp
await mediator.Publish(new OrderPlacedNotification(orderId, 150m), options =>
{
    options.Delivery = NotificationDelivery.Queue;
});
```

* **Default Backing Queue**: The mediator registers an internal, bounded `System.Threading.Channels.Channel` via `ChannelMessageQueue`.
* **Using a Custom Queue (`IMessageQueue`)**: To replace the in-memory channel with a persistent database or disk-backed queue, implement `IMessageQueue` and register it in DI:

```csharp
public class CustomDatabaseMessageQueue : IMessageQueue
{
    public async ValueTask EnqueueAsync(NotificationEnvelope envelope, CancellationToken cancellationToken)
    {
        // Persist envelope to a database table or custom storage
    }

    public async IAsyncEnumerable<DequeuedMessage> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Poll or stream envelopes from storage, yielding DequeuedMessage with an IMessageAckToken
        yield break;
    }
}

// In DI configuration:
builder.Services.AddSingleton<IMessageQueue, CustomDatabaseMessageQueue>();
```

* **Retry Budget & Backoff**: Configured via `options.QueueOptions` with exponential backoff and randomized jitter.
* **Failure Handling**: Failed attempts notify `INotificationErrorHandler` and route to `IDeadLetterSink` when retries are exhausted.

> [!NOTE]
> **Execution Mode & Scope**: Queue mode runs asynchronously inside background pump workers (`NotificationQueuePump`). Because there is no ambient web request scope, **each handler executes in parallel with its own isolated `IServiceScope`**. Furthermore, handlers that succeed on earlier attempts are tracked and skipped during retries, ensuring only failed handlers re-execute.

### 3. Broker Delivery (Cross-Service Pub/Sub)

```mermaid
flowchart LR
    Pub(["Publisher"]) --> Broker[("Message Broker<br/>RabbitMQ / Redis")]
    Broker --> Top["Topic / Stream"]
    Top --> Sub1["Billing Service Handler"]
    Top --> Sub2["Inventory Service Handler"]
```

Broadcasts notifications across distributed microservices. Requires the corresponding official extension transport package:

| Package | Transport | Capabilities | Status |
| :--- | :--- | :--- | :--- |
| [`AwadyLab.Mediator.RabbitMQ`](https://www.nuget.org/packages/AwadyLab.Mediator.RabbitMQ) | RabbitMQ (AMQP 0-9-1) | Durable queues, topic/fanout exchanges, publisher confirms, channel pooling | [![NuGet](https://img.shields.io/nuget/v/AwadyLab.Mediator.RabbitMQ.svg)](https://www.nuget.org/packages/AwadyLab.Mediator.RabbitMQ) |
| [`AwadyLab.Mediator.Redis`](https://www.nuget.org/packages/AwadyLab.Mediator.Redis) | Redis Streams | Consumer groups, auto-claim for crashed workers, approximate stream trimming | [![NuGet](https://img.shields.io/nuget/v/AwadyLab.Mediator.Redis.svg)](https://www.nuget.org/packages/AwadyLab.Mediator.Redis) |

Register the transport package in the `AddMediator` configure callback:

```csharp
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;

builder.Services.AddMediator([typeof(Program).Assembly], options =>
{
    options.UseRabbitMq(new RabbitMqOptions
    {
        ConnectionString = "amqp://guest:guest@localhost:5672/",
        Broker = new RabbitMqBrokerOptions() // enables NotificationDelivery.Broker
    });
});
```

#### Customizing Broker Behavior & Core Interfaces

To customize broker messaging, the following interfaces are available:

* `IMessageBroker`: Core transport contract implemented by `AwadyLab.Mediator.RabbitMQ` and `AwadyLab.Mediator.Redis`. You can implement `IMessageBroker` (and `IReconnectableMessageBroker`) to support other brokers like Azure Service Bus, AWS SQS/SNS, or Apache Kafka.
* `IExternalNotificationDefinition<T>`: Declares where a notification is published (exchange or stream) as an external contract.
* `IExternalNotificationHandler<T>`: Implemented by consuming services, declaring their subscription `QueueName`.
* `IMessageSerializer`: Serializes wire payloads (default: `SystemTextJsonMessageSerializer`). Replace by registering your own `IMessageSerializer` in DI (e.g., MessagePack or Protobuf).
* `IDeadLetterSink`: Captures permanently failed envelopes after retries are exhausted.

```csharp
using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models.Options;

public sealed class OrderPlacedDefinition : IExternalNotificationDefinition<OrderPlacedNotification>
{
    public void Define(ExternalNotificationOptions options)
    {
        options.RabbitMq.Exchange = "orders.placed";
        options.Redis.Stream = "orders.placed";
    }
}

[ExternalSubscription(Prefetch = 20, MaxRetries = 5)]
public class BillingOrderHandler(IBillingService billing) : IExternalNotificationHandler<OrderPlacedNotification>
{
    public static string QueueName => "billing-service";

    public async Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken) =>
        await billing.ProcessInvoiceAsync(notification.OrderId, notification.Amount, cancellationToken);
}
```

* **Security Sanitization**: Message headers automatically strip CRLF injection characters and Unicode directional override attacks.
* **Type Security**: Deserialization is constrained to registered types to prevent arbitrary type injection.
* **Auto-Resubscribe**: External subscriptions automatically restore on network reconnection.

> [!NOTE]
> **Execution Mode & Scope**: Inbound broker messages are received directly by the background subscription worker (`BrokerSubscriptionService`). Handlers execute in **parallel with an isolated `IServiceScope` per handler**, completely independent of any caller or HTTP request context.

---

## 4. Unified Facade (`IMediator`)

Inject `IMediator` into API endpoints or Controllers. Each method simply forwards to the corresponding
mediator (`IRequestMediator`, `IStreamMediator`, `INotificationPublisher`) — if you never register a
handler for one of those surfaces, `AddMediator` still registers `IMediator` itself, and calling that
method throws an `InvalidOperationException` naming the missing service instead of a
`NullReferenceException`.

```csharp
// CreateOrderCommand : IRequest<CreateOrderResult>, and CreateOrderResult : IResponse — defined the same
// way as GetUserQuery/UserDto above.
[ApiController]
[Route("api/[controller]")]
public class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command, CancellationToken ct)
    {
        // 1. Request / Response
        var result = await mediator.Execute(command, ct);

        // 2. Publish Notification — the second positional parameter is Action<PublishOptions>?, so a
        // CancellationToken has to be passed by name.
        await mediator.Publish(new OrderPlacedNotification(result.OrderId, result.Total), cancellationToken: ct);

        return Ok(result);
    }

    [HttpGet("stream")]
    public IAsyncEnumerable<MetricPoint> StreamMetrics(CancellationToken ct) =>
        // 3. Async Streaming
        mediator.ExecuteAsync(new StreamMetricsRequest(42), ct);
}
```

---

## Configuration Reference

### Options Reference Table

The following tables list all configurable options across the mediator:

#### `MediatorOptions`

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `DefaultNotificationDelivery` | `NotificationDelivery` | `Direct` | Fallback delivery mode when `PublishOptions.Delivery` is omitted. |
| `ExecutorPipelines` | `List<Type>` | `[]` | Pipeline behaviors (`IPipelineBehaviorHandler<,>`) wrapping request handlers in registration order. |
| `NotificationPipelines` | `List<Type>` | `[]` | Pipeline behaviors (`INotificationPipelineBehavior<>`) wrapping notification handlers. |
| `TypeFilter` | `Func<Type, bool>?` | `null` | Optional predicate to filter discovered handler/executor types during assembly scanning. |
| `StreamRequestDecorator` | `Func<IStreamRequest, CancellationToken, ValueTask<IStreamRequest?>>?` | `null` | Intercepts, transforms, or vetoes stream requests (`null` yields break) before handler execution. |
| `StreamResponseDecorator` | `Func<IResponse, CancellationToken, ValueTask<IResponse?>>?` | `null` | Intercepts, transforms, or drops individual streamed items (`null` drops the item). |
| `UseNotificationQueue(...)` | `Action<NotificationQueueOptions>?` | Disabled | Enables the in-memory background channel queue and worker pump. |
| `UseExternalSubscriptionDefaults(...)` | `Action<ExternalSubscriptionOptions>` | Defaults | Fallback subscription settings for external handlers lacking `[ExternalSubscription]`. |

#### `NotificationQueueOptions` (Configured via `UseNotificationQueue`)

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Capacity` | `int` | `10_000` | Bounded capacity of the in-memory queue channel. |
| `FullMode` | `BoundedChannelFullMode` | `Wait` | Channel behavior when capacity is reached (`Wait`, `DropOldest`, etc.). |
| `MaxConcurrency` | `int` | `4` | Maximum notifications the background pump dispatches concurrently. |
| `MaxRetries` | `int` | `3` | Maximum attempts for a failing notification before dead-lettering (1 = try once, no retries). |
| `RetryDelay` | `TimeSpan` | `1s` | Initial delay before the first retry attempt. |
| `UseExponentialBackoff` | `bool` | `true` | Doubles retry wait on each successive attempt (up to `MaxRetryDelay`). |
| `MaxRetryDelay` | `TimeSpan` | `30s` | Maximum duration ceiling for any single retry wait. |
| `RetryJitter` | `double` | `0.2` (20%) | Randomized delay fraction (0.0–1.0) added to spread out concurrent retries. |
| `DeadLetterEnabled` | `bool` | `true` | When true, exhausted envelopes route to the registered `IDeadLetterSink`. |
| `QueueRestartDelay` | `TimeSpan` | `5s` | Duration a worker waits before restarting its loop after a transport fault. |
| `MaxInlineRetryDuration` | `TimeSpan` | `30s` | Maximum total duration an envelope may spend retrying before being dead-lettered. |

#### `PublishOptions` (Configured per `Publish` call)

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Delivery` | `NotificationDelivery?` | `null` (inherits default) | Target delivery mode (`Direct`, `Queue`, or `Broker`). |
| `ExecuteParallel` | `bool` | `true` | Direct mode execution: `true` for concurrent isolated-scope dispatch; `false` for sequential ambient-scope dispatch. |

### Configuration Example

```csharp
builder.Services.AddMediator([typeof(Program).Assembly], options =>
{
    // Custom predicate for discovered handler types
    options.TypeFilter = type => !type.Name.StartsWith("Test");

    // Pipeline behaviors for IRequestMediator
    options.ExecutorPipelines.Add(typeof(LoggingPipeline<,>));

    // Pipeline behaviors for INotificationPublisher
    options.NotificationPipelines.Add(typeof(NotificationAuditPipeline<>));

    // Default notification delivery mode (Direct, Queue, or Broker)
    options.DefaultNotificationDelivery = NotificationDelivery.Direct;

    // Queue & Retry Configuration
    options.UseNotificationQueue(queue =>
    {
        queue.Capacity = 5_000;
        queue.MaxConcurrency = 4;
        queue.MaxRetries = 3;
        queue.RetryDelay = TimeSpan.FromMilliseconds(200);
        queue.MaxRetryDelay = TimeSpan.FromSeconds(5);
    });

    // Streaming Decorators with pattern matching
    options.StreamRequestDecorator = async (req, _) => req;
    options.StreamResponseDecorator = async (resp, _) => resp;
});
```

---

## License

Package by Mohamed Elawady
```
Copyright (c) Mohamed Elawady. All rights reserved.

This software is closed source and proprietary. Subject to the terms below,
you are granted a free, worldwide, non-exclusive license to:

  - Use this software, in source or compiled (NuGet package) form, in your
    own applications, including commercial applications, at no charge.

You may NOT:

  - Redistribute, sublicense, sell, or publish this software (or any
    modified version of it) as a standalone product, library, or package,
    including republishing it under a different name on NuGet.org or any
    other package repository.
  - Reverse engineer, decompile, or disassemble the compiled package except
    to the extent applicable law expressly permits this despite this
    limitation.
  - Remove or alter any copyright, trademark, or other proprietary notices.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

```