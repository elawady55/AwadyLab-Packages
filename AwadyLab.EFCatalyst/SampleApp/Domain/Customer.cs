using AwadyLab.EFCatalyst.Domain;
using AwadyLab.EFCatalyst.Entities;

namespace SampleApp.Domain;

/// <summary>A tenant-owned, audited entity with sensitive columns (see <c>CustomerConfiguration</c>).</summary>
public sealed class Customer : AuditedEntity<int>, ITenantEntity<int>
{
    public Customer()
    {
    }

    public Customer(int id, string name, string email, string phone, string loyaltyCode)
        : base(id)
    {
        Name = name;
        Email = email;
        Phone = phone;
        LoyaltyCode = loyaltyCode;
    }

    public string Name { get; set; } = "";

    /// <summary>Encrypted deterministically, so equality lookups still work.</summary>
    public string Email { get; set; } = "";

    /// <summary>Stored masked: only the last digits survive.</summary>
    public string Phone { get; set; } = "";

    /// <summary>Stored as a keyed, format-preserving permutation.</summary>
    public string LoyaltyCode { get; set; } = "";

    /// <summary>Set by the save pipeline from the context's current tenant.</summary>
    public int TenantId { get; private set; }
}
