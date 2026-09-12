using AwadyLab.Mediator.Abstraction;

namespace SampleApp.ServiceB.Models;

/// <summary>
/// Queue-delivery demo backed by Redis: published with <c>Delivery = Queue</c>, but because this service
/// configures <c>RedisOptions.Queue</c> (and no in-memory channel), the envelope lands in a durable Redis
/// Stream instead — it survives a restart of this process, unlike Service A's <c>/queue</c> demo.
/// </summary>
public sealed record ShipmentQueuedNotification(Guid OrderId, string Carrier) : INotification;
