using AwadyLab.Mediator.Abstraction;

namespace SampleApp.ServiceA.Models;

/// <summary>
/// Queue-delivery demo: enqueued to the internal in-memory channel and drained asynchronously by
/// <c>NotificationQueuePump</c>, instead of running inline like <see cref="InventoryCheckedNotification"/>.
/// A separate notification type from the Direct demo — every handler registered for a notification type
/// runs regardless of which <c>Delivery</c> a given publish call used, so distinguishing Direct from Queue
/// here needs two notification types, not one type with two handlers.
/// </summary>
public sealed record StockReorderRequestedNotification(string Sku, int Quantity) : INotification;
