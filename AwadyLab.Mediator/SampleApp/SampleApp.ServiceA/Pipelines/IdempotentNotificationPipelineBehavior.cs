using System.Collections.Concurrent;
using System.Reflection;
using AwadyLab.Mediator.Abstraction;

namespace SampleApp.ServiceA.Pipelines;

/// <summary>
/// A notification pipeline behavior that enforces idempotency for notifications marked with
/// <see cref="IdempotentAttribute"/>.
/// </summary>
public sealed class IdempotentNotificationPipelineBehavior<TNotification>(
    ILogger<IdempotentNotificationPipelineBehavior<TNotification>> logger)
    : INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    // Thread-safe in-memory store tracking processed notification keys.
    // In distributed production setups, this can be backed by Redis or an external database.
    private static readonly ConcurrentDictionary<string, byte> ProcessedKeys = new();

    public async Task HandleAsync(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var attribute = typeof(TNotification).GetCustomAttribute<IdempotentAttribute>();
        if (attribute is null)
        {
            // Unmarked notification: pass straight through without idempotency checks
            await next();
            return;
        }

        var key = ExtractKey(notification, attribute.KeyProperty);
        var cacheKey = $"{typeof(TNotification).Name}:{key}";

        if (!ProcessedKeys.TryAdd(cacheKey, 0))
        {
            logger.LogWarning(
                "[Pipeline:Idempotent] Duplicate notification detected for {NotificationType} with key '{Key}'. Skipping handlers.",
                typeof(TNotification).Name, key);
            return; // Deduplicated: short-circuit without calling next()
        }

        logger.LogInformation(
            "[Pipeline:Idempotent] First time seeing notification {NotificationType} with key '{Key}'. Dispatching to handlers.",
            typeof(TNotification).Name, key);

        await next();
    }

    private static string ExtractKey(TNotification notification, string? configuredProperty)
    {
        var type = typeof(TNotification);

        if (!string.IsNullOrWhiteSpace(configuredProperty))
        {
            var prop = type.GetProperty(configuredProperty, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(notification)?.ToString() ?? string.Empty;
        }

        foreach (var candidate in new[] { "Id", "Key", "Sku" })
        {
            var prop = type.GetProperty(candidate, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
                return prop.GetValue(notification)?.ToString() ?? string.Empty;
        }

        return notification.ToString() ?? type.Name;
    }
}
