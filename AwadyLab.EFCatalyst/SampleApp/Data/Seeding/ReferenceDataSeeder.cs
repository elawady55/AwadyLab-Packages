using AwadyLab.EFCatalyst.Abstractions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;

namespace SampleApp.Data.Seeding;

/// <summary>
/// Runs once per database through EF Core's seeding hooks (<c>Migrate</c>/<c>EnsureCreated</c>), in its own
/// transaction, and is recorded in <c>__EFCatalystSeedHistory</c>. The context belongs to the scope that created the
/// database (tenant 1), so the tenant guard stamps the customers with tenant 1.
/// </summary>
public sealed class ReferenceDataSeeder : IMigrationSeeder
{
    public string Id => "reference/catalog-and-customers";

    public int Order => 0;

    public string? AfterMigration => null;

    public async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        context.AddRange(
            new Product(1, "Keyboard", 49.90m),
            new Product(2, "Mouse", 19.90m),
            new Product(3, "Monitor", 199m),
            new Product(4, "Floppy drive", 9.90m) { IsActive = false });

        context.AddRange(
            new Category { Id = 1, Name = "Electronics" },
            new Category { Id = 2, ParentId = 1, Name = "Computers" },
            new Category { Id = 3, ParentId = 2, Name = "Laptops" },
            new Category { Id = 4, ParentId = 2, Name = "Accessories" },
            new Category { Id = 5, ParentId = 1, Name = "Phones" },
            new Category { Id = 6, ParentId = 1, Name = "Pagers", IsArchived = true });

        context.AddRange(
            new Customer(1, "Ann", "ann@example.com", "+47 555 01 234", "AB-1234"),
            new Customer(2, "Bob", "bob@example.com", "+47 555 09 876", "CD-5678"));

        await context.SaveChangesAsync(cancellationToken);
    }
}
