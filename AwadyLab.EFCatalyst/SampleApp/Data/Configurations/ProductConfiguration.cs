using AwadyLab.EFCatalyst.Configurations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleApp.Domain;

namespace SampleApp.Data.Configurations;

public sealed class ProductConfiguration : EntityConfigurationBase<Product, int>
{
    /// <summary>A shadow property: stored and queryable, but not part of the CLR type.</summary>
    public const string ImportedFrom = "ImportedFrom";

    protected override void ConfigureEntity(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property<string?>(ImportedFrom).HasMaxLength(50);
    }
}
