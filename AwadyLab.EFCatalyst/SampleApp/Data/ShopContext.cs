using AwadyLab.EFCatalyst.Abstractions;
using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Data.Configurations;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Data;

/// <summary>
/// A plain DbContext: multi-tenancy lives on the entities (<c>ITenantEntity&lt;int&gt;</c>) and the current tenant comes
/// from the registered <c>ICurrentTenantAccessor&lt;int&gt;</c> (<see cref="TenantSession"/>).
/// </summary>
public sealed class ShopContext(DbContextOptions<ShopContext> options, IColumnEncryptionKeyProvider keys) : DbContext(options)
{
    public DbSet<Customer> Customers { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<Product> Products { get; set; }

    public DbSet<Category> Categories { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder
            .ConfigureDecimalPrecision(18, 2)
            .ConfigureDateTimeUtc();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity configuration first, then the filters (they are applied to the entity types that exist).
        modelBuilder.ApplyConfiguration(new CustomerConfiguration(keys));
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());

        modelBuilder.AddDefaultQueryFilters(this);                                            // soft delete + active state + tenant
        modelBuilder.AddGlobalQueryFilterInterfacesInAssembly(typeof(ShopContext).Assembly, this); // ArchivedFilter
    }
}
