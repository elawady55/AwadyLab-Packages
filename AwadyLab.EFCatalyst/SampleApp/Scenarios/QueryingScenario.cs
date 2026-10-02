using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Querying: conditional composition, EXISTS/IN, client sorting, paging and global filters.</summary>
public sealed class QueryingScenario : IScenario
{
    public string Name => "querying";

    public string Title => "Querying: conditional filters, sorting, paging and global filters";

    public async Task RunAsync(SampleHost host)
    {
        await using (var setup = host.Open())
        {
            for (var id = 1; id <= 7; id++)
            {
                setup.Db.Orders.Add(new Order(id, customerId: id % 2 + 1, total: id * 10m));
            }

            await setup.Db.SaveChangesAsync();
        }

        await using var session = host.Open();
        var db = session.Db;

        Output.Step("Conditional composition: filters apply only when the input is set (typical search form)");
        string? search = "o";
        decimal? minTotal = null;
        int[] ids = [1, 2, 3, 4, 5];
        var matches = await db.Customers
            .WhereIfNotNullOrWhiteSpace(search, c => c.Name.Contains(search!))
            .WhereIf(minTotal is not null, c => c.Id > 0)
            .WhereIn(c => c.Id, ids)
            .WhereExists(db.Orders, (c, o) => o.CustomerId == c.Id && o.Total > 50)
            .Select(c => c.Name)
            .ToListAsync();
        Output.Line($"customers named like '{search}' with an order over 50: {string.Join(", ", matches)}");

        Output.Step("Client-supplied sorting, restricted to an allow-list");
        string[] allowed = ["Total", "Id"];
        var sorted = await db.Orders.ApplySorting("total desc, id", allowed).Select(o => o.Id).Take(3).ToListAsync();
        Output.Line($"'total desc, id': {string.Join(", ", sorted)}");
        try
        {
            db.Orders.ApplySorting("CreatedBy", allowed);
        }
        catch (ArgumentException exception)
        {
            Output.Error(exception);
        }

        Output.Step("Paging: offset (with total count) and keyset (stable cursors)");
        var page = await db.Orders.OrderBy(o => o.Id).ToPagedListAsync(pageIndex: 1, pageSize: 3);
        Output.Line($"offset page {page.PageIndex + 1}/{page.TotalPages}: {string.Join(", ", page.Items.Select(o => o.Id))} of {page.TotalCount}");
        var first = await db.Orders.ToCursorPageAsync(o => o.Total, o => o.Id, cursor: null, size: 3);
        var second = await db.Orders.ToCursorPageAsync(o => o.Total, o => o.Id, first.NextCursor, size: 3);
        Output.Line($"keyset: [{string.Join(", ", first.Items.Select(o => o.Id))}] -> [{string.Join(", ", second.Items.Select(o => o.Id))}], more: {second.HasMore}");

        Output.Step("Global filters: built-in (active state) and application (archived), each switchable on its own");
        Output.Line($"active products:  {string.Join(", ", await db.Products.Select(p => p.Name).ToListAsync())}");
        Output.Line($"all categories:   {await db.Categories.CountAsync()} visible, " +
                    $"{await db.Categories.IgnoreQueryFilter<Category, IArchivable>().CountAsync()} including archived");
    }
}
