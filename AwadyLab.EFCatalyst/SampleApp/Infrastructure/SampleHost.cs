using System.Security.Cryptography;
using AwadyLab.EFCatalyst.Abstractions;
using AwadyLab.EFCatalyst.Extensions;
using AwadyLab.EFCatalyst.Options;
using AwadyLab.EFCatalyst.Services;
using AwadyLab.EFCatalyst.Sqlite.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SampleApp.Data;
using SampleApp.Data.Seeding;
using SampleApp.Domain;

namespace SampleApp.Infrastructure;

/// <summary>
/// The composition root: how an application wires EFCatalyst into dependency injection. Each scenario gets a freshly
/// seeded SQLite database file.
/// </summary>
public sealed class SampleHost : IAsyncDisposable
{
    // Demo only: a random key per run, shared by every host of the run. Load real keys from a secret store (never
    // from source control); keep old key ids in the ring so rows written before a rotation stay readable.
    private static readonly ColumnEncryptionKeyRing Keys = new(
        "2026-09", new Dictionary<string, byte[]> { ["2026-09"] = RandomNumberGenerator.GetBytes(32) });

    private readonly ServiceProvider _services;

    private SampleHost(ServiceProvider services, string databasePath)
    {
        _services = services;
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    /// <summary>Builds the host; <paramref name="configure"/> tweaks the EFCatalyst options for one scenario.</summary>
    public static SampleHost Create(Action<EFCatalystOptions>? configure = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "efcatalyst-sample.db");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new EFCatalystConsoleLoggerProvider()));

        // 1. EFCatalyst: options, interceptors and TimeProvider. Features act only on entities implementing their
        //    contract, so the defaults rarely need changing; validation is opt-in.
        services.AddEFCatalyst(o =>
        {
            o.Features.Validation = true;
            configure?.Invoke(o);
        });

        // 2. What the application provides: keys, the current user, the tenant and the event dispatcher.
        services.AddSingleton<IColumnEncryptionKeyProvider>(Keys);
        services.AddScoped<ICurrentTenantAccessor<int>,TenantSession>();
        services.AddScoped<ICurrentUserAccessor,CurrentUser>();
        services.AddScoped<IDomainEventDispatcher, ConsoleDomainEventDispatcher>();
        services.AddSingleton<IMigrationSeeder, ReferenceDataSeeder>();

        // 3. The context: the provider package, the interceptors and the seeders.
        services.AddDbContext<ShopContext>((sp, options) => options
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .UseEFCatalystSqlite()
            .UseEFCatalystInterceptors(sp)
            .UseEFCatalystSeeding(sp));

        return new SampleHost(services.BuildServiceProvider(), databasePath);
    }

    /// <summary>Opens a unit of work for a tenant and a user (in a web app: one request).</summary>
    public Session Open(int tenant = 1, string user = "alice")
    {
        var scope = _services.CreateAsyncScope();
        // The same scoped instances EFCatalyst reads; a web app's accessors would read the request instead.
        ((TenantSession)scope.ServiceProvider.GetRequiredService<ICurrentTenantAccessor<int>>()).TenantId = tenant;
        ((CurrentUser)scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>()).UserId = user;
        return new Session(scope);
    }

    /// <summary>Recreates the database (the seeder adds the reference data) and adds a customer of tenant 2.</summary>
    public async Task ResetAsync()
    {
        await using (var session = Open(tenant: 1, user: "seeder"))
        {
            await session.Db.EnsureDatabaseDeletedAndRecreatedAsync();
        }

        await using (var session = Open(tenant: 2, user: "zoe"))
        {
            session.Db.Customers.Add(new Customer(3, "Zed", "zed@example.com", "+46 555 11 111", "EF-9012"));
            await session.Db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    public sealed class Session(AsyncServiceScope scope) : IAsyncDisposable
    {
        public ShopContext Db { get; } = scope.ServiceProvider.GetRequiredService<ShopContext>();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
