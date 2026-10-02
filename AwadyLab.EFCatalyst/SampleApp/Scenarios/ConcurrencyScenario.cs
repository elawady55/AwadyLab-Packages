using AwadyLab.EFCatalyst.Exceptions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Optimistic concurrency: IRowVersion tokens (rotated by EFCatalyst where the database does not).</summary>
public sealed class ConcurrencyScenario : IScenario
{
    public string Name => "concurrency";

    public string Title => "Optimistic concurrency with IRowVersion";

    public async Task RunAsync(SampleHost host)
    {
        await using (var setup = host.Open())
        {
            setup.Db.Orders.Add(new Order(1, 1, 10m));
            await setup.Db.SaveChangesAsync();
        }

        await using var first = host.Open(user: "alice");
        await using var second = host.Open(user: "bob");
        var mine = await first.Db.Orders.SingleAsync();
        var theirs = await second.Db.Orders.SingleAsync();

        mine.Total = 11m;
        await first.Db.SaveChangesAsync();
        Output.Line("alice saved first");

        theirs.Total = 12m;
        try
        {
            await second.Db.SaveChangesAsync();
        }
        catch (EFCatalystConcurrencyException exception)
        {
            // A DbUpdateConcurrencyException, so existing retry/merge code keeps working.
            Output.Error(exception);
        }
    }
}
