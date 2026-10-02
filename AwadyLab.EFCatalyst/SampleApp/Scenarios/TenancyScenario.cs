using AwadyLab.EFCatalyst.Exceptions;
using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Multi-tenancy: the tenant filter on reads and the tenant guard on writes.</summary>
public sealed class TenancyScenario : IScenario
{
    public string Name => "tenancy";

    public string Title => "Multi-tenancy: tenant filter and tenant guard";

    public async Task RunAsync(SampleHost host)
    {
        await using (var tenant1 = host.Open(tenant: 1))
        {
            Output.Step("Each tenant sees only its own rows");
            Output.Line($"tenant 1: {string.Join(", ", await tenant1.Db.Customers.OrderBy(c => c.Id).Select(c => c.Name).ToListAsync())}");

            Output.Step("New rows get the current tenant automatically");
            var customer = new Customer(10, "Cleo", "cleo@example.com", "+47 555 22 333", "GH-3456");
            tenant1.Db.Customers.Add(customer);
            await tenant1.Db.SaveChangesAsync();
            Output.Line($"Cleo.TenantId = {customer.TenantId}");

            Output.Step("Writing another tenant's row is rejected, even when it was loaded explicitly");
            var foreign = await tenant1.Db.Customers.IgnoreTenantFilter().SingleAsync(c => c.Name == "Zed");
            foreign.Name = "Hacked";
            try
            {
                await tenant1.Db.SaveChangesAsync();
            }
            catch (TenantViolationException exception)
            {
                Output.Error(exception);
            }
        }

        await using (var tenant2 = host.Open(tenant: 2, user: "zoe"))
        {
            Output.Line($"tenant 2: {string.Join(", ", await tenant2.Db.Customers.Select(c => c.Name).ToListAsync())}");
        }

        await using var admin = host.Open(tenant: 0, user: "admin");
        Output.Step("A host context bypasses the filter explicitly (the query is tagged for auditing)");
        var all = admin.Db.Customers.IgnoreTenantFilter().OrderBy(c => c.Id);
        Output.Line($"all tenants: {string.Join(", ", await all.Select(c => $"{c.Name}@{c.TenantId}").ToListAsync())}");
        Output.Line($"SQL starts with: {all.ToQueryString().Split('\n')[0]}");
    }
}
