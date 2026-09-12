using AwadyLab.Mediator.Abstraction.Messaging;
using SampleApp.Contracts;

namespace SampleApp.ServiceB.Handlers;

/// <summary>
/// Consumes <see cref="OrderPlacedNotification"/> over RabbitMQ — fires whenever Service A's
/// <c>POST /orders</c> publishes, with no endpoint of its own to trigger it. Watch this service's console
/// after calling Service A.
/// </summary>
[ExternalSubscription(Prefetch = 10, MaxRetries = 3)]
public sealed class OrderPlacedExternalHandler(ILogger<OrderPlacedExternalHandler> logger)
    : IExternalNotificationHandler<OrderPlacedNotification>
{
    // Identifies this subscriber's own RabbitMQ queue, bound to the "orders.events" exchange declared by
    // OrderPlacedDefinition in the shared Contracts project.
    public static string QueueName => "service-b.orders";

    public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Broker/RabbitMQ] Received order {OrderId} from Service A: {CustomerName}, total {Total:C}.",
            notification.OrderId, notification.CustomerName, notification.Total);
        return Task.CompletedTask;
    }
}
