using System.Text.Json;
using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Data.Configurations;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Change tracking: inspecting pending changes, the audit log and shadow properties.</summary>
public sealed class ChangeTrackingScenario : IScenario
{
    public string Name => "change-tracking";

    public string Title => "Change tracking, audit log and shadow properties";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open(user: "carol");
        var db = session.Db;

        var ann = await db.Customers.SingleAsync(c => c.Id == 1);
        ann.Name = "Anna";
        ann.Email = "anna@example.com";
        db.Orders.Add(new Order(1, customerId: 1, total: 99m));

        Output.Step("Inspecting an entry before saving");
        var entry = db.Entry(ann);
        Output.Line($"Name changed: {entry.HasPropertyChanged(c => c.Name)}, was '{entry.GetOriginalValue(c => c.Name)}'");
        Output.Line($"changed properties: {string.Join(", ", entry.GetChangedProperties().Select(c => c.PropertyName))}");

        Output.Step("Audit log (user and clock from DI; sensitive values redacted)");
        var audit = db.ChangeTracker.ToAuditEntryList();
        await db.SaveChangesAsync();
        audit.ResolveTemporaryKeys();
        foreach (var item in audit)
        {
            Output.Line(JsonSerializer.Serialize(new
            {
                item.Action,
                item.EntityName,
                item.KeyValues,
                item.UserId,
                Changes = item.Changes.Where(c => c.PropertyName is "Name" or "Email" or "Total")
                    .Select(c => $"{c.PropertyName}: {c.OriginalValue ?? "∅"} -> {c.CurrentValue}"),
            }));
        }

        Output.Step("Shadow properties: stored and queryable without a CLR member");
        var keyboard = await db.Products.SingleAsync(p => p.Id == 1);
        db.SetShadowProperty(keyboard, ProductConfiguration.ImportedFrom, "legacy-erp");
        await db.SaveChangesAsync();
        var imported = await db.Products.WhereShadowEquals<Product, string?>(ProductConfiguration.ImportedFrom, "legacy-erp").ToListAsync();
        Output.Line($"imported from legacy-erp: {string.Join(", ", imported.Select(p => p.Name))} " +
                    $"({db.GetShadowProperty<string?>(keyboard, ProductConfiguration.ImportedFrom)})");
    }
}
