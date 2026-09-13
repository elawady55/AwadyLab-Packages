using AwadyLab.Mediator;
using AwadyLab.Mediator.Abstraction;
using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Enums;
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;
using AwadyLab.Mediator.Redis;
using AwadyLab.Mediator.Redis.Options;
using SampleApp.Contracts;
using SampleApp.ServiceB.Metrics;
using SampleApp.ServiceB.Models;
using SampleApp.ServiceB.Pipelines;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? "amqp://guest:guest@localhost:5672/";

// Register Performance & Observability Metrics Collector and Custom Handlers
builder.Services.AddSingleton<ServiceBMetrics>();
builder.Services.AddSingleton<INotificationErrorHandler, MetricsNotificationErrorHandler>();
builder.Services.AddSingleton<IDeadLetterSink, MetricsDeadLetterSink>();

builder.Services.AddMediator([typeof(Program).Assembly, typeof(OrderPlacedNotification).Assembly], options =>
{
    // Notification pipeline behavior: measures latency, active in-flight executions, and success/failure metrics
    options.NotificationPipelines.Add(typeof(PerformanceMetricsNotificationPipelineBehavior<>));

    // Configure retry parameters for background queue processing
    options.UseNotificationQueue(queue =>
    {
        queue.MaxRetries = 3;
        queue.RetryDelay = TimeSpan.FromMilliseconds(300);
        queue.MaxRetryDelay = TimeSpan.FromSeconds(2);
    });

    // Redis Queue mode: a durable Redis Stream backs NotificationDelivery.Queue instead of the in-memory
    // channel Service A uses — this is the only queue this service has, so /shipments always goes through it.
    options.UseRedis(new RedisOptions
    {
        ConnectionMultiplexerFactory = async () => await ConnectionMultiplexer.ConnectAsync(redisConnectionString),
        Queue = new RedisQueueOptions { StreamKey = "serviceb:queue" }
    });

    // RabbitMQ Broker mode, consume-only: no Queue set here, so this only enables subscribing to
    // OrderPlacedNotification via OrderPlacedExternalHandler — never used to publish from this service.
    options.UseRabbitMq(new RabbitMqOptions
    {
        ConnectionString = rabbitMqConnectionString,
        Broker = new RabbitMqBrokerOptions()
    });
});

var app = builder.Build();

// Expose JSON Metrics Dashboard for live observability
app.MapGet("/metrics", (ServiceBMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

// Reset metrics counters for clean interactive testing
app.MapPost("/metrics/reset", (ServiceBMetrics metrics) =>
{
    metrics.Reset();
    return Results.Ok(new { message = "Metrics counters successfully reset." });
});

app.MapPost("/shipments", async (IMediator mediator, bool simulateFailure = false, CancellationToken cancellationToken = default) =>
{
    var orderId = Guid.NewGuid();
    await mediator.Publish(
        new ShipmentQueuedNotification(orderId, "AwadyLab Express", SimulateFailure: simulateFailure),
        options => options.Delivery = NotificationDelivery.Queue,
        cancellationToken);

    if (simulateFailure)
    {
        return Results.Ok(new
        {
            orderId,
            simulateFailure = true,
            status = "Enqueued with simulated failure",
            note = "Watch console for retry attempts (1..3) and dead-letter sink routing, then inspect GET /metrics."
        });
    }

    return Results.Ok(new
    {
        orderId,
        simulateFailure = false,
        status = "Enqueued to durable Redis Stream",
        note = "ShipmentQueuedHandler will run off the background pump, and duration/success will be recorded in GET /metrics."
    });
});

app.Run();
