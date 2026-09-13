using System.Diagnostics;
using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceB.Metrics;

namespace SampleApp.ServiceB.Pipelines;

/// <summary>
/// A notification pipeline behavior that measures execution duration, active in-flight counts,
/// and records success/failure metrics for all processed notifications.
/// </summary>
public sealed class PerformanceMetricsNotificationPipelineBehavior<TNotification>(
    ServiceBMetrics metrics,
    ILogger<PerformanceMetricsNotificationPipelineBehavior<TNotification>> logger)
    : INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    public async Task HandleAsync(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var typeName = typeof(TNotification).Name;
        metrics.RecordExecutionStart(typeName);
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next();
            var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.RecordExecutionCompleted(typeName, durationMs, success: true);

            logger.LogInformation(
                "[Metrics:Pipeline] Notification {NotificationType} processed successfully in {Duration:F2} ms.",
                typeName, durationMs);
        }
        catch (Exception ex)
        {
            var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.RecordExecutionCompleted(typeName, durationMs, success: false);

            logger.LogWarning(
                "[Metrics:Pipeline] Notification {NotificationType} failed after {Duration:F2} ms: {ErrorMessage}",
                typeName, durationMs, ex.Message);

            throw; // Re-throw to allow EnvelopeProcessor retry logic to engage
        }
    }
}
