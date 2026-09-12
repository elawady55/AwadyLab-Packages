using AwadyLab.Mediator;
using AwadyLab.Mediator.Abstraction;
using AwadyLab.Mediator.Enums;
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;
using AwadyLab.Mediator.Redis;
using AwadyLab.Mediator.Redis.Options;
using SampleApp.Contracts;
using SampleApp.ServiceB.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? "amqp://guest:guest@localhost:5672/";

builder.Services.AddMediator([typeof(Program).Assembly, typeof(OrderPlacedNotification).Assembly], options =>
{
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

app.MapPost("/shipments", async (IMediator mediator, CancellationToken cancellationToken) =>
{
    await mediator.Publish(new ShipmentQueuedNotification(Guid.NewGuid(), "AwadyLab Express"),
        options => options.Delivery = NotificationDelivery.Queue, cancellationToken);

    return Results.Ok("Enqueued to the durable Redis Stream — ShipmentQueuedHandler will run off the background pump.");
});

app.Run();
