# AwadyLab.Mediator.RabbitMQ

RabbitMQ queue and distributed pub/sub broker transport for [AwadyLab.Mediator](https://www.nuget.org/packages/AwadyLab.Mediator).

[![NuGet](https://img.shields.io/nuget/v/AwadyLab.Mediator.RabbitMQ.svg)](https://www.nuget.org/packages/AwadyLab.Mediator.RabbitMQ)
![Tests](https://img.shields.io/badge/tests-passing-brightgreen.svg)
![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Language](https://img.shields.io/badge/C%23-14-blue.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

`AwadyLab.Mediator.RabbitMQ` integrates [RabbitMQ](https://www.rabbitmq.com/) with `AwadyLab.Mediator`, supporting two distinct messaging topologies over a single shared connection:

```mermaid
flowchart TD
    Mediator{{"AwadyLab.Mediator"}}
    
    Mediator -->|"Delivery = Queue"| QMode["Queue Mode<br/>(RabbitMqMessageQueue)"]
    Mediator -->|"Delivery = Broker"| BMode["Broker Mode<br/>(RabbitMqBroker)"]
    
    QMode --> SingleQueue[("Durable Service Queue<br/>x-dead-letter-exchange")]
    BMode --> TopicEx["Topic / Fanout Exchange"]
    
    SingleQueue --> Pump["Internal Queue Pump"]
    TopicEx --> SubQ1[("Consumer Queue A")]
    TopicEx --> SubQ2[("Consumer Queue B")]
    
    SubQ1 --> H1{{"IExternalNotificationHandler&lt;&gt;"}}
    SubQ2 --> H2{{"IExternalNotificationHandler&lt;&gt;"}}
```

1. **Queue Mode (`NotificationDelivery.Queue`)**: Replaces the default in-memory channel queue with a durable RabbitMQ queue (`RabbitMqMessageQueue`). Enqueued messages survive application restarts and are processed by local worker loops.
2. **Broker Mode (`NotificationDelivery.Broker`)**: Full cross-service pub/sub messaging across independent microservices (`RabbitMqBroker` and `RabbitMqSubscription`) with automatic topology declaration, publisher confirms, and reconnect recovery.

You can enable **Queue Mode**, **Broker Mode**, or **Both** simultaneously over a single AMQP connection.

---

## Features & Reliability

* **Publisher Confirms**: Enabled by default (`PublisherConfirms = true`). Publishes synchronously await RabbitMQ broker acknowledgment before returning.
* **Channel Pooling**: Publishes share a managed pool of channels (`PublishChannelPoolSize = 8` by default) to eliminate channel churn and maximize throughput.
* **Resilient Reconnections**: Automatically detects network loss or broker restarts, restores connection, and recreates subscriptions without dropping workers.
* **Header Sanitization**: Prevents CRLF injection and Unicode directional override vulnerabilities across envelope headers.
* **Dead-Letter Support**: RabbitMQ rejected messages route to `DeadLetterExchange`, while exhausted retry attempts route to `IDeadLetterSink`.

---

## Installation

```bash
dotnet add package AwadyLab.Mediator.RabbitMQ
```

---

## Quickstart

### 1. Register in DI (`Program.cs`)

```csharp
using AwadyLab.Mediator;
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator([typeof(Program).Assembly], options =>
{
    options.UseRabbitMq(new RabbitMqOptions
    {
        ConnectionString = "amqp://guest:guest@localhost:5672/",
        
        // 1. Enable Broker Pub/Sub Mode
        Broker = new RabbitMqBrokerOptions
        {
            Prefetch = 20,
            DeadLetterExchange = "dlx.events"
        },
        
        // 2. (Optional) Enable Durable Queue Mode for local queue delivery
        Queue = new RabbitMqQueueOptions
        {
            QueueName = "orders-service-queue",
            Prefetch = 10
        }
    });
});

var app = builder.Build();
```

---

## Broker Mode (Cross-Service Pub/Sub)

In Broker mode, events published with `NotificationDelivery.Broker` are serialized and broadcast to an exchange. Subscribing microservices consume events through their own durable queues.

### 1. Define Notification & Topology

Implement `IExternalNotificationDefinition<T>` to declare the exchange contract:

```csharp
using AwadyLab.Mediator.Abstraction;
using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models.Options;
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;

// Domain event contract
public record OrderPlacedNotification(Guid OrderId, decimal Total) : INotification;

// Topology definition (exchange contract)
public sealed class OrderPlacedDefinition : IExternalNotificationDefinition<OrderPlacedNotification>
{
    public void Define(ExternalNotificationOptions options)
    {
        // Declares topic exchange and optional routing rules
        options.RabbitMq.Exchange = "orders.events";
        options.RabbitMq.Mode = RabbitMqExchangeMode.Topic;
        options.RabbitMq.RoutingKey = "orders.placed";
    }
}
```

`Mode` also has `Fanout` (the default — every bound queue gets every message, routing key/headers ignored) and `Headers` (a queue binds on message headers instead of a routing key, via `options.RabbitMq.BindingHeaders` and `MatchAll`). `Mode` is fixed for the life of the exchange — RabbitMQ refuses to redeclare an existing exchange with a different type — so, like `Exchange` itself, it belongs to the notification's shared topology and can't be varied per publish; only the values the chosen mode routes *on* (routing key, or headers) can be overridden per call.

### 2. Implement External Subscriber Handler

Consumers implement `IExternalNotificationHandler<T>` and specify a static `QueueName`:

```csharp
using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models.Options;

[ExternalSubscription(Prefetch = 25, MaxRetries = 3)]
public class BillingServiceHandler(IBillingService billing) 
    : IExternalNotificationHandler<OrderPlacedNotification>
{
    // Required: Identifies the subscription queue bound to the exchange
    public static string QueueName => "billing-service.orders";

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await billing.GenerateInvoiceAsync(notification.OrderId, notification.Total, ct);
    }
}
```

### 3. Publishing to the Broker

```csharp
await mediator.Publish(new OrderPlacedNotification(orderId, 250m), options =>
{
    options.Delivery = NotificationDelivery.Broker;
    
    // Optional: Set per-message RabbitMQ publish overrides — behaviour only, never topology (the exchange
    // and its routing mode are fixed by the notification's IExternalNotificationDefinition, not per call).
    options.RabbitMq.RoutingKey = "orders.placed.eu"; // topic-mode exchanges only
    options.RabbitMq.Priority = 2;                    // requires the queue declared with x-max-priority
    options.RabbitMq.Expiration = TimeSpan.FromMinutes(30);
    options.RabbitMq.Headers["region"] = "eu";        // matched by headers-mode bindings, carried as metadata otherwise
});
```

---

## Queue Mode (Durable In-Process Channel)

When `RabbitMqOptions.Queue` is configured, `NotificationDelivery.Queue` routes envelopes through a durable RabbitMQ queue instead of an in-memory channel:

```csharp
await mediator.Publish(new OrderPlacedNotification(orderId, 250m), options =>
{
    options.Delivery = NotificationDelivery.Queue;
});
```

* Survives service restarts.
* Drained by the mediator's background `NotificationQueuePump` workers with exponential backoff retries.

---

## Options Reference

### `RabbitMqOptions`

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ConnectionString` | `string` | *(Required)* | AMQP URI (`amqp://` or `amqps://`). Validated at startup. |
| `PublisherConfirms` | `bool` | `true` | Requests publisher confirmation for every publish. |
| `PublishChannelPoolSize`| `int` | `8` | Size of the pooled channels for concurrent publishing. |
| `ReconnectDelay` | `TimeSpan` | `5s` | Wait duration between connection retry attempts. |
| `Broker` | `RabbitMqBrokerOptions?` | `null` | Enables cross-service pub/sub broker mode when configured. |
| `Queue` | `RabbitMqQueueOptions?` | `null` | Enables durable queue mode for `NotificationDelivery.Queue`. |

### `RabbitMqBrokerOptions`

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Prefetch` | `ushort` | `10` | Default prefetch count for consumer subscriptions. |
| `DeadLetterExchange` | `string?` | `null` | Sets `x-dead-letter-exchange` on declared subscription queues. |

### `RabbitMqQueueOptions`

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `QueueName` | `string?` | `null` | Custom name for the queue (auto-generated if omitted). |
| `Durable` | `bool` | `true` | Whether the queue survives broker restarts. |
| `Prefetch` | `ushort` | `10` | Consumer prefetch count for queue pump workers. |
| `DeadLetterQueue` | `string?` | `null` | DLQ routing for unhandled rejected messages. |

---

## Two Dead-Letter Mechanisms — Don't Confuse Them

RabbitMQ mode has two independent "give up" paths, configured in different places:

* **`options.RabbitMq.DeadLetterExchange`** (per-notification, falling back to `RabbitMqBrokerOptions.DeadLetterExchange`) is RabbitMQ's own `x-dead-letter-exchange` queue argument. It fires when the **broker itself rejects** a message — most commonly a payload that can't be deserialized at all and so never reaches a handler. This is transport-level and has nothing to do with retry counts.
* **`[ExternalSubscription(...)].RepublishExhaustedTo`** (see the main [`AwadyLab.Mediator`](https://www.nuget.org/packages/AwadyLab.Mediator) README) is an application-level hand-off: a message whose **handler** kept failing until `MaxRetries` was exhausted gets republished there and then acked, so RabbitMQ considers it delivered. If `RepublishExhaustedTo` is left unset, an exhausted message just falls to `IDeadLetterSink` (logged by default — see `AwadyLab.Mediator`'s `LoggingDeadLetterSink`) with no broker-side trace.

Configure both if you want coverage for both failure modes — a bad payload never reaches either without `DeadLetterExchange`, and a handler that keeps throwing never reaches either without `RepublishExhaustedTo` or a durable `IDeadLetterSink`.

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
