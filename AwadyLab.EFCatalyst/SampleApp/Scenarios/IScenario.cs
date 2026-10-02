using AwadyLab.EFCatalyst.Options;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>One feature area of EFCatalyst, run against a freshly seeded database.</summary>
public interface IScenario
{
    /// <summary>Gets the name used to run this scenario alone: <c>dotnet run --project SampleApp -- &lt;name&gt;</c>.</summary>
    string Name { get; }

    /// <summary>Gets the heading printed before the scenario.</summary>
    string Title { get; }

    /// <summary>Adjusts the EFCatalyst options for this scenario; most scenarios use the defaults.</summary>
    void Configure(EFCatalystOptions options)
    {
    }

    Task RunAsync(SampleHost host);
}
