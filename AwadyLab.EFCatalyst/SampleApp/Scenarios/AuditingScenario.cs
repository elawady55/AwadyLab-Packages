using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Audit stamping: who created and last changed a row, and when (from ICurrentUserAccessor and TimeProvider).</summary>
public sealed class AuditingScenario : IScenario
{
    public string Name => "auditing";

    public string Title => "Audit stamping";

    public async Task RunAsync(SampleHost host)
    {
        await using (var alice = host.Open(user: "alice"))
        {
            alice.Db.Orders.Add(new Order(1, customerId: 1, total: 25m));
            await alice.Db.SaveChangesAsync();
        }

        await using (var bob = host.Open(user: "bob"))
        {
            var order = await bob.Db.Orders.SingleAsync();
            order.Total = 30m;

            // Creation audit is immutable: an attempt to rewrite it is reverted by the save pipeline.
            bob.Db.Entry(order).Property(o => o.CreatedBy).CurrentValue = "mallory";
            await bob.Db.SaveChangesAsync();
        }

        await using var reader = host.Open();
        var saved = await reader.Db.Orders.AsNoTracking().SingleAsync();
        Output.Line($"created by {saved.CreatedBy} at {saved.CreatedAtUtc:u}");
        Output.Line($"modified by {saved.LastModifiedBy} at {saved.LastModifiedAtUtc:u}");
    }
}
