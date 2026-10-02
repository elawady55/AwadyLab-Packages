using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SampleApp.Data;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Column security: what is stored versus what the application reads.</summary>
public sealed class ColumnSecurityScenario : IScenario
{
    public string Name => "security";

    public string Title => "Column security: encrypted, masked and permutated columns";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        var db = session.Db;

        Output.Step("Stored in the database (read with raw SQL)");
        Output.Line($"Email (AES-256-GCM, deterministic): {Shorten(await RawAsync(db, nameof(Customer.Email)))}");
        Output.Line($"Phone (masked, one-way):             {await RawAsync(db, nameof(Customer.Phone))}");
        Output.Line($"LoyaltyCode (permutated):            {await RawAsync(db, nameof(Customer.LoyaltyCode))}");

        Output.Step("Read through EF Core");
        var ann = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == 1);
        Output.Line($"Email={ann.Email}, Phone={ann.Phone}, LoyaltyCode={ann.LoyaltyCode}");

        Output.Step("Deterministic encryption keeps equality lookups working");
        Output.Line($"customer with bob@example.com: {(await db.Customers.SingleAsync(c => c.Email == "bob@example.com")).Name}");
    }

    // Names come from the schema extensions, so the raw SQL follows the model's mapping.
    private static async Task<string> RawAsync(ShopContext db, string property)
    {
        var value = db.GetService<ISqlGenerationHelper>().DelimitIdentifier("Value");
        var sql = $"SELECT {db.GetDelimitedColumnName<Customer>(property)} AS {value} FROM {db.GetDelimitedTableName<Customer>()} " +
                  $"WHERE {db.GetDelimitedColumnName<Customer>(nameof(Customer.Id))} = 1";
        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync();
    }

    private static string Shorten(string value) => value.Length > 40 ? value[..40] + "…" : value;
}
