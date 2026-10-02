using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Scenarios;

/// <summary>Model building: contract configurators, configuration bases, conventions and schema reflection.</summary>
public sealed class ModelScenario : IScenario
{
    public string Name => "model";

    public string Title => "Model building and schema reflection";

    public async Task RunAsync(SampleHost host)
    {
        await using var session = host.Open();
        var db = session.Db;

        Output.Step("Contracts configure themselves (EntityConfigurationBase applies a configurator per interface)");
        var order = db.Model.FindEntityType(typeof(Order))!;
        Output.Line($"Order.RowVersion is a concurrency token: {order.FindProperty(nameof(Order.RowVersion))!.IsConcurrencyToken}");
        Output.Line($"Order.TenantId is indexed:              {order.GetIndexes().Any(i => i.Properties[0].Name == nameof(Order.TenantId))}");
        Output.Line($"Order.CreatedBy max length:             {order.FindProperty(nameof(Order.CreatedBy))!.GetMaxLength()} (UserIdMaxLength override)");
        Output.Line($"Order.IsDeleted database default:       {order.FindProperty(nameof(Order.IsDeleted))!.GetDefaultValue()}");
        Output.Line($"Order.DomainEvents is mapped:           {order.FindProperty(nameof(Order.DomainEvents)) is not null}");
        Output.Line($"Product.IntegrityHash max length:       {db.Model.FindEntityType(typeof(Product))!.FindProperty(nameof(Product.IntegrityHash))!.GetMaxLength()}");

        Output.Step("Conventions: decimal precision for every decimal, UTC for every DateTime");
        var total = order.FindProperty(nameof(Order.Total))!;
        Output.Line($"Order.Total precision: ({total.GetPrecision()}, {total.GetScale()})");

        Output.Step("Schema reflection follows the mapping (ToTable, HasColumnName)");
        Output.Line($"table:           {db.GetTableName<Category>()}  quoted: {db.GetDelimitedTableName<Category>()}");
        Output.Line($"ParentId column: {db.GetColumnName<Category>(c => c.ParentId)}  quoted: {db.GetDelimitedColumnName<Category>(c => c.ParentId)}");
        Output.Line($"Order keys:      {string.Join(", ", db.GetPrimaryKeys<Order>())}");
        foreach (var property in db.GetMappedProperties<Customer>().Take(4))
        {
            Output.Line($"Customer.{property.PropertyName,-10} -> {property.ColumnName} ({property.ColumnType}{(property.IsNullable ? ", null" : "")})");
        }
    }
}
