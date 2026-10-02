using AwadyLab.EFCatalyst.Exceptions;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Validation: DataAnnotations checked before saving (opt-in with Features.Validation).</summary>
public sealed class ValidationScenario : IScenario
{
    public string Name => "validation";

    public string Title => "Validation before saving";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        session.Db.Products.Add(new Product(10, name: "", price: 0m));
        session.Db.Products.Add(new Product(11, name: new string('x', 60), price: 5m));
        try
        {
            await session.Db.SaveChangesAsync();
        }
        catch (EFCatalystValidationException exception)
        {
            Output.Error(exception);
            foreach (var error in exception.Errors)
            {
                Output.Line($"{error.EntityName}.{string.Join(",", error.MemberNames)}: {error.ErrorMessage}");
            }
        }
    }
}
