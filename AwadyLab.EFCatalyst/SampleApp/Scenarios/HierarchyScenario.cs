using AwadyLab.EFCatalyst.Extensions;
using AwadyLab.EFCatalyst.Options;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Recursive hierarchy queries over a self-referencing table.</summary>
public sealed class HierarchyScenario : IScenario
{
    public string Name => "hierarchy";

    public string Title => "Recursive hierarchies: descendants and ancestors";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        var categories = session.Db.Categories;

        Output.Step("Descendants of Electronics (the archived 'Pagers' is hidden by its filter)");
        foreach (var (category, depth) in await categories.GetDescendantsAsync(1, c => c.Id, c => c.ParentId))
        {
            Output.Line($"{new string(' ', depth * 2)}{category.Name} (depth {depth})");
        }

        Output.Step("Ancestors of Laptops, for breadcrumbs");
        var crumbs = await categories.GetAncestorsAsync(3, c => c.Id, c => c.ParentId,
            new HierarchyQueryOptions<Domain.Category> { IncludeSelf = true });
        Output.Line(string.Join(" > ", crumbs.Reverse().Select(i => i.Entity.Name)));
    }
}
