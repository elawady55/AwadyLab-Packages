using AwadyLab.Mediator.Abstraction.Messaging;
using AwadyLab.Mediator.Models.Options;
using AwadyLab.Mediator.RabbitMQ;

namespace SampleApp.Contracts;

/// <summary>
/// The RabbitMQ topology for <see cref="OrderPlacedNotification"/> — both services scan this same class
/// (it lives in the shared Contracts assembly), so they agree on the exchange without either one having to
/// hardcode the other's configuration. Fanout is the default <c>Mode</c>, which is enough here: Service B
/// is the only subscriber, so no routing key is needed.
/// </summary>
public sealed class OrderPlacedDefinition : IExternalNotificationDefinition<OrderPlacedNotification>
{
    public void Define(ExternalNotificationOptions options) =>
        options.RabbitMq.Exchange = "orders.events";
}
