using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleApp.Domain;

namespace SampleApp.Data.Configurations;

/// <summary>Custom table and column names: EFCatalyst's raw SQL (hierarchies, helpers) picks them up from the model.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("product_categories");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ParentId).HasColumnName("parent_id");
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId);
    }
}
