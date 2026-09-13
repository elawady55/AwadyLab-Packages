using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceB.Models;

namespace SampleApp.ServiceB.Handlers;

/// <summary>Runs off <c>NotificationQueuePump</c>, drained from the durable Redis Stream this time.</summary>
public sealed class ShipmentQueuedHandler(ILogger<ShipmentQueuedHandler> logger)
    : INotificationHandler<ShipmentQueuedNotification>
{
    public Task Handle(ShipmentQueuedNotification notification, CancellationToken cancellationToken)
    {
        if (notification.SimulateFailure)
        {
            logger.LogWarning(
                "[Redis Queue] Simulating handler failure for shipment of order {OrderId} to demonstrate retries and error metrics...",
                notification.OrderId);
            throw new InvalidOperationException($"Simulated processing failure for order {notification.OrderId}");
        }

        logger.LogInformation(
            "[Redis Queue] Shipment queued for order {OrderId} via {Carrier}.",
            notification.OrderId, notification.Carrier);
        return Task.CompletedTask;
    }
}
