using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceA.Pipelines;

namespace SampleApp.ServiceA.Models;

/// <summary>
/// Queue-delivery demo: enqueued to the internal in-memory channel and drained asynchronously by
/// <c>NotificationQueuePump</c>, instead of running inline like <see cref="InventoryCheckedNotification"/>.
/// Marked with [Idempotent(nameof(Sku))] so the notification pipeline deduplicates reorders with the same SKU.
/// </summary>
[Idempotent(nameof(Sku))]
public sealed record StockReorderRequestedNotification(string Sku, int Quantity) : INotification;
