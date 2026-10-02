using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Seeding and maintenance: run-once seeders and pending-migration checks.</summary>
public sealed class SeedingScenario : IScenario
{
    public string Name => "seeding";

    public string Title => "Seeding (IMigrationSeeder) and maintenance";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        var db = session.Db;

        Output.Step("The seeder ran while the database was created, and was recorded");
        var sql = db.GetService<ISqlGenerationHelper>();
        var history = $"SELECT {sql.DelimitIdentifier("SeederId")} AS {sql.DelimitIdentifier("Value")} FROM {sql.DelimitIdentifier("__EFCatalystSeedHistory")}";
        var applied = await db.Database
            .SqlQueryRaw<string>(history)
            .ToListAsync();
        Output.Line($"seed history: {string.Join(", ", applied)}; products: {await db.Products.IgnoreQueryFilters().CountAsync()}");

        Output.Step("Creating/migrating again runs the seeding hook, but a recorded seeder is skipped");
        await db.Database.EnsureCreatedAsync();
        Output.Line($"products: {await db.Products.IgnoreQueryFilters().CountAsync()}");

        Output.Step("Readiness check");
        Output.Line($"pending migrations: {await db.HasPendingMigrationsAsync()} (this sample uses EnsureCreated, so none)");
    }
}
