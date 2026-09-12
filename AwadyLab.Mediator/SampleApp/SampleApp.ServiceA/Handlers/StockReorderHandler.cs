using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceA.Models;

namespace SampleApp.ServiceA.Handlers;

/// <summary>
/// Runs off the background <c>NotificationQueuePump</c>, after <c>POST /queue</c> has already returned —
/// watch the console: the log line for this appears a moment after the HTTP response, not during it.
/// </summary>
public sealed class StockReorderHandler(ILogger<StockReorderHandler> logger)
    : INotificationHandler<StockReorderRequestedNotification>
{
    public async Task Handle(StockReorderRequestedNotification notification, CancellationToken cancellationToken)
    {
        // Simulated work, to make the "this ran later, off the request thread" point visible.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        logger.LogInformation(
            "[Queue] Stock reorder processed off the background pump for {Sku}: {Quantity} requested.",
            notification.Sku, notification.Quantity);
    }
}
