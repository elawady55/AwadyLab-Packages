using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Domain events: raised by aggregates, dispatched only after the save succeeded.</summary>
public sealed class DomainEventsScenario : IScenario
{
    public string Name => "events";

    public string Title => "Domain events dispatched after a successful save";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        for (var id = 1; id <= 3; id++)
        {
            var order = new Order(id, customerId: 1, total: id * 10m);
            order.Place();
            session.Db.Orders.Add(order);
        }

        Output.Line("saving…");
        await session.Db.SaveChangesAsync();
        Output.Line("saved; the events were cleared from the aggregates");
    }
}
