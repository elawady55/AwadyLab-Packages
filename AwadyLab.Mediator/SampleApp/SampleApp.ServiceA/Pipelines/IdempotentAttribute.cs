namespace SampleApp.ServiceA.Pipelines;

/// <summary>
/// Marks a notification type as requiring idempotent processing.
/// Duplicate publications with the same key will be deduplicated and skipped by the pipeline.
/// </summary>
/// <param name="keyProperty">Optional property name on the notification to extract the idempotency key from.
/// If omitted, the pipeline inspects common key properties ("Id", "Key", "Sku") or falls back to the notification itself.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IdempotentAttribute(string? keyProperty = null) : Attribute
{
    public string? KeyProperty { get; } = keyProperty;
}
