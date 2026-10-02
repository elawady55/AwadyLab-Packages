using AwadyLab.EFCatalyst.Abstractions;
using AwadyLab.EFCatalyst.Configurations;
using AwadyLab.EFCatalyst.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleApp.Domain;

namespace SampleApp.Data.Configurations;

/// <summary>
/// The base class maps the key and every contract the entity implements (tenant index, audit columns); this class
/// only adds what is specific to customers, including column security, which is configured with the fluent API only.
/// </summary>
public sealed class CustomerConfiguration(IColumnEncryptionKeyProvider keys) : EntityConfigurationBase<Customer, int>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Email).StoreEncrypted(keys, deterministic: true);
        builder.Property(c => c.Phone).StoreMasked(visibleSuffix: 3);
        builder.Property(c => c.LoyaltyCode).StorePermutated(keys);
    }
}
