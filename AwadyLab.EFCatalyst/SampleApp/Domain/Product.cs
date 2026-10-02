using System.ComponentModel.DataAnnotations;
using AwadyLab.EFCatalyst.Domain;
using AwadyLab.EFCatalyst.Entities;

namespace SampleApp.Domain;

/// <summary>
/// A shared (not tenant-owned) entity that can be switched off (<see cref="IActiveState"/>), is validated before
/// saving (DataAnnotations) and carries an integrity hash that detects changes made outside the application.
/// </summary>
public sealed class Product : Entity<int>, IActiveState, IHashableEntity
{
    public Product()
    {
    }

    public Product(int id, string name, decimal price)
        : base(id)
    {
        Name = name;
        Price = price;
    }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = "";

    [Range(typeof(decimal), "0.01", "10000")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public string? IntegrityHash { get; set; }
}
