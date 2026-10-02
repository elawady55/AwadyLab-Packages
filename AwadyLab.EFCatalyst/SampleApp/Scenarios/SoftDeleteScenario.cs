using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Soft delete: Remove becomes an update, plus restore, bulk operations and permanent deletion.</summary>
public sealed class SoftDeleteScenario : IScenario
{
    public string Name => "soft-delete";

    public string Title => "Soft delete, restore and permanent delete";

    public async Task RunAsync(SampleHost host)
    {
        await using (var setup = host.Open())
        {
            setup.Db.Orders.AddRange(new Order(1, 1, 10m), new Order(2, 1, 20m), new Order(3, 2, 30m), new Order(4, 2, 40m));
            await setup.Db.SaveChangesAsync();
        }

        await using var session = host.Open(user: "bob");
        var db = session.Db;

        Output.Step("Remove() sets IsDeleted and the deletion audit instead of deleting");
        db.Orders.Remove(await db.Orders.SingleAsync(o => o.Id == 1));
        await db.SaveChangesAsync();
        var deleted = await db.Orders.OnlySoftDeleted().SingleAsync();
        Output.Line($"visible: {await db.Orders.CountAsync()}, soft-deleted: #{deleted.Id} by {deleted.DeletedBy}");

        Output.Step("Restore() brings a loaded row back and clears the deletion audit on save");
        db.Restore(deleted);
        await db.SaveChangesAsync();
        Output.Line($"#{deleted.Id}: IsDeleted={deleted.IsDeleted}, DeletedBy={deleted.DeletedBy ?? "null"}, modified by {deleted.LastModifiedBy}");

        Output.Step("Bulk: one UPDATE each (ExecuteDeleteAsync would bypass soft delete)");
        Output.Line($"soft-deleted: {await db.Orders.Where(o => o.Total > 15).ExecuteSoftDeleteAsync("bob")}");
        Output.Line($"restored:     {await db.Orders.Where(o => o.Id == 2).ExecuteRestoreAsync("bob")}");
        Output.Line($"visible: {await db.Orders.CountAsync()}, including deleted: {await db.Orders.IncludeSoftDeleted().CountAsync()}");

        Output.Step("RemovePermanently() really deletes (e.g. an erasure request)");
        db.ChangeTracker.Clear();
        db.RemovePermanently(await db.Orders.IncludeSoftDeleted().SingleAsync(o => o.Id == 4));
        await db.SaveChangesAsync();
        Output.Line($"rows left including deleted: {await db.Orders.IncludeSoftDeleted().CountAsync()}");
    }
}
