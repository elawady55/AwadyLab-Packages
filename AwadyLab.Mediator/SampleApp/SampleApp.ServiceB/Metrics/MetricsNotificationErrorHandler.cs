using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models;

namespace SampleApp.ServiceB.Metrics;

/// <summary>
/// Intercepts failed notification dispatches from Redis Queue and RabbitMQ Broker, recording retry
/// attempts into <see cref="ServiceBMetrics"/> while providing structured logging.
/// </summary>
public sealed class MetricsNotificationErrorHandler(
    ServiceBMetrics metrics,
    ILogger<MetricsNotificationErrorHandler> logger)
    : INotificationErrorHandler
{
    public Task OnDispatchFailedAsync(
        NotificationEnvelope envelope,
        Exception exception,
        bool willRetry,
        CancellationToken cancellationToken)
    {
        var notificationName = envelope.NotificationType.Name;
        metrics.RecordRetry(notificationName, envelope.Attempt, willRetry);

        if (willRetry)
        {
            logger.LogWarning(
                "[Metrics:Retry] Attempt #{Attempt} for {NotificationType} (MessageId: {MessageId}) failed: {ErrorMessage}. Will retry according to backoff policy.",
                envelope.Attempt, notificationName, envelope.MessageId, exception.Message);
        }
        else
        {
            logger.LogError(
                "[Metrics:RetryExhausted] Attempt #{Attempt} for {NotificationType} (MessageId: {MessageId}) failed: {ErrorMessage}. Retries exhausted; transferring to dead-letter sink.",
                envelope.Attempt, notificationName, envelope.MessageId, exception.Message);
        }

        return Task.CompletedTask;
    }
}
