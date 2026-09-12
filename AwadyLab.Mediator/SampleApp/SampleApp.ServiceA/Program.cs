using AwadyLab.Mediator;
using AwadyLab.Mediator.Abstraction;
using AwadyLab.Mediator.Enums;
using AwadyLab.Mediator.RabbitMQ;
using AwadyLab.Mediator.RabbitMQ.Options;
using SampleApp.Contracts;
using SampleApp.ServiceA.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator([typeof(Program).Assembly, typeof(OrderPlacedNotification).Assembly], options =>
{
    // Queue mode: the internal in-memory channel that backs /queue. Explicit here (rather than left to
    // AddMediator's automatic enable-on-broker fallback) so the intent reads clearly next to UseRabbitMq.
    options.UseNotificationQueue();

    // Broker mode only — Service A never routes NotificationDelivery.Queue over RabbitMQ itself (that's
    // the in-memory channel's job above); RabbitMQ here exists purely to publish OrderPlacedNotification
    // out to whichever services, like Service B, are listening.
    options.UseRabbitMq(new RabbitMqOptions
    {
        ConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
            ?? "amqp://guest:guest@localhost:5672/",
        Broker = new RabbitMqBrokerOptions()
    });
});

var app = builder.Build();

app.MapPost("/direct", async (IMediator mediator, CancellationToken cancellationToken) =>
{
    await mediator.Publish(new InventoryCheckedNotification("WIDGET-1", 42),
        options => options.Delivery = NotificationDelivery.Direct, cancellationToken);

    return Results.Ok("Published with Direct delivery — InventoryCheckedHandler already ran before this response.");
});

app.MapPost("/queue", async (IMediator mediator, CancellationToken cancellationToken) =>
{
    await mediator.Publish(new StockReorderRequestedNotification("WIDGET-1", 100),
        options => options.Delivery = NotificationDelivery.Queue, cancellationToken);

    return Results.Ok("Enqueued with Queue delivery — StockReorderHandler will run off the background pump; check this service's console in a moment.");
});

app.MapPost("/orders", async (IMediator mediator, CreateOrderRequest request, CancellationToken cancellationToken) =>
{
    var result = await mediator.Execute(new CreateOrderCommand(request.CustomerName, request.Sku, request.Quantity),
        cancellationToken);

    // Broker delivery: no handler for OrderPlacedNotification exists in this service at all — Service A is
    // a producer-only participant in this notification's topology. See ProducerOnlyContractTests in the
    // core test suite for the case this mirrors.
    await mediator.Publish(new OrderPlacedNotification(result.OrderId, result.CustomerName, result.Total),
        options => options.Delivery = NotificationDelivery.Broker, cancellationToken);

    return Results.Ok(result);
});

app.Run();

public sealed record CreateOrderRequest(string CustomerName, string Sku, int Quantity);
