using AwadyLab.EFCatalyst.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleApp.Domain;

namespace SampleApp.Data.Configurations;

/// <summary>The audited base also lets you size the user columns (<c>CreatedBy</c>, <c>DeletedBy</c>, ...).</summary>
public sealed class OrderConfiguration : AuditedEntityConfigurationBase<Order, int>
{
    protected override int UserIdMaxLength => 64;

    protected override void ConfigureEntity(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId);
    }
}
