using AwadyLab.EFCatalyst.Exceptions;
using AwadyLab.EFCatalyst.Extensions;
using AwadyLab.EFCatalyst.Options;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Integrity hashes: IHashableEntity rows carry an HMAC that is checked on every load.</summary>
public sealed class IntegrityScenario : IScenario
{
    public string Name => "integrity";

    public string Title => "Integrity hashes detect changes made outside the application";

    public async Task RunAsync(SampleHost host)
    {
        await using (var session = host.Open())
        {
            var monitor = await session.Db.Products.AsNoTracking().SingleAsync(p => p.Id == 3);
            Output.Line($"Monitor costs {monitor.Price}; hash {monitor.IntegrityHash![..20]}…");

            Output.Step("Someone changes the price directly in the database");
            var db = session.Db;
            // Only names from the model are concatenated; they are quoted by the provider.
            var tamper =
                $"UPDATE {db.GetDelimitedTableName<Product>()} SET {db.GetDelimitedColumnName<Product>(p => p.Price)} = 1 " +
                $"WHERE {db.GetDelimitedColumnName<Product>(p => p.Id)} = 3";
            await db.Database.ExecuteSqlRawAsync(tamper);

            Output.Step("Default behaviour: the violation is logged (event 41003) and the row is still returned");
            var tampered = await db.Products.AsNoTracking().SingleAsync(p => p.Id == 3);
            Output.Line($"loaded price: {tampered.Price}");
        }

        Output.Step("IntegrityViolationBehavior.Throw refuses to load the row");
        await using var strict = SampleHost.Create(o => o.IntegrityViolationBehavior = IntegrityViolationBehavior.Throw);
        await using var strictSession = strict.Open();
        try
        {
            await strictSession.Db.Products.AsNoTracking().SingleAsync(p => p.Id == 3);
        }
        catch (IntegrityViolationException exception)
        {
            Output.Error(exception);
        }
    }
}
