using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models;

namespace SampleApp.ServiceB.Metrics;

/// <summary>
/// Terminal sink for notifications whose retries were exhausted, recording dead-letter metrics
/// in <see cref="ServiceBMetrics"/> and logging diagnostic information.
/// </summary>
public sealed class MetricsDeadLetterSink(
    ServiceBMetrics metrics,
    ILogger<MetricsDeadLetterSink> logger)
    : IDeadLetterSink
{
    public Task SendAsync(
        NotificationEnvelope envelope,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        var notificationName = envelope.NotificationType.Name;
        metrics.RecordDeadLetter(notificationName);

        logger.LogError(
            "[Metrics:DeadLetter] Notification {NotificationType} (MessageId: {MessageId}, Origin: {Origin}) permanently failed after {Attempts} attempts. Routed to Dead-Letter Sink. Last error: {ErrorMessage}",
            notificationName, envelope.MessageId, envelope.Origin, envelope.Attempt, lastException.Message);

        return Task.CompletedTask;
    }
}
