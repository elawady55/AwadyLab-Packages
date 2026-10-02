using SampleApp.Infrastructure;
using SampleApp.Scenarios;

// A tour of AwadyLab.EFCatalyst on a local SQLite file, one scenario per feature area.
//   dotnet run --project SampleApp                 runs every scenario
//   dotnet run --project SampleApp -- tenancy      runs one (see the names below)
IScenario[] scenarios =
[
    new ModelScenario(),
    new TenancyScenario(),
    new AuditingScenario(),
    new SoftDeleteScenario(),
    new ColumnSecurityScenario(),
    new IntegrityScenario(),
    new ConcurrencyScenario(),
    new ValidationScenario(),
    new DomainEventsScenario(),
    new ChangeTrackingScenario(),
    new QueryingScenario(),
    new HierarchyScenario(),
    new SeedingScenario(),
    new MigrationHelpersScenario(),
    new DiagnosticsScenario(),
];

var selected = args.Length == 0
    ? scenarios
    : scenarios.Where(s => args.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
if (selected.Length == 0)
{
    Console.WriteLine($"Unknown scenario. Available: {string.Join(", ", scenarios.Select(s => s.Name))}");
    return 1;
}

foreach (var scenario in selected)
{
    // Each scenario gets its own host (its options) and a freshly seeded database.
    await using var host = SampleHost.Create(scenario.Configure);
    Output.Muted = true;
    await host.ResetAsync();
    Output.Muted = false;
    Output.Title($"{scenario.Title}  [{scenario.Name}]");
    await scenario.RunAsync(host);
}

return 0;
