using AwadyLab.Mediator.Abstraction;

namespace SampleApp.Contracts;

/// <summary>
/// The one event that actually crosses a process boundary in this sample — published by Service A over
/// RabbitMQ broker delivery, consumed by Service B's <c>IExternalNotificationHandler&lt;OrderPlacedNotification&gt;</c>.
/// Lives in this shared project because both services must compile against the exact same contract.
/// </summary>
public sealed record OrderPlacedNotification(Guid OrderId, string CustomerName, decimal Total) : INotification;
