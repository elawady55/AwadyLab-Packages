using AwadyLab.EFCatalyst.Options;
using Microsoft.EntityFrameworkCore;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>
/// Command interceptors: query tagging (a correlation comment on every command) and slow-query logging. The
/// threshold is zero here so every command is logged; in production keep a real threshold (default 1 s).
/// Deadlock retry is on by default and retries commands outside transactions.
/// </summary>
public sealed class DiagnosticsScenario : IScenario
{
    public string Name => "diagnostics";

    public string Title => "Diagnostics: query tags and slow-query logging";

    public void Configure(EFCatalystOptions options)
    {
        options.Features.QueryTagging = true;
        options.QueryTagApplicationName = "SampleApp";
        options.QueryTagIncludesUser = true;
        options.SlowQueryThreshold = TimeSpan.Zero;
    }

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open(tenant: 1, user: "alice");
        Output.Step("One query: the logged command starts with the EFCatalyst tag (app, tenant, user)");
        var count = await session.Db.Customers.CountAsync();
        Output.Line($"customers: {count}");
    }
}
