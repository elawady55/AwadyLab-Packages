using AwadyLab.EFCatalyst.Abstractions;
using AwadyLab.EFCatalyst.Domain;
using AwadyLab.EFCatalyst.Entities;

namespace SampleApp.Domain;

/// <summary>Raised when an order is placed; dispatched after the save succeeds.</summary>
public sealed record OrderPlaced(int OrderId, decimal Total) : IDomainEvent;

/// <summary>
/// An aggregate root that is fully audited (creation, modification, soft deletion), tenant-owned and protected by an
/// optimistic-concurrency token.
/// </summary>
public sealed class Order : FullAuditedAggregateRoot<int>, ITenantEntity<int>, IRowVersion
{
    public Order()
    {
    }

    public Order(int id, int customerId, decimal total)
        : base(id)
    {
        CustomerId = customerId;
        Total = total;
    }

    public int CustomerId { get; private set; }

    public decimal Total { get; set; }

    public int TenantId { get; private set; }

    public byte[] RowVersion { get; set; } = [];

    public void Place() => AddDomainEvent(new OrderPlaced(Id, Total));
}
