using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceA.Models;

namespace SampleApp.ServiceA.Handlers;

/// <summary>Runs in-process, synchronously, as part of the <c>POST /direct</c> request itself.</summary>
public sealed class InventoryCheckedHandler(ILogger<InventoryCheckedHandler> logger)
    : INotificationHandler<InventoryCheckedNotification>
{
    public Task Handle(InventoryCheckedNotification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Direct] Inventory checked in-process for {Sku}: {Quantity} on hand.",
            notification.Sku, notification.QuantityOnHand);
        return Task.CompletedTask;
    }
}
