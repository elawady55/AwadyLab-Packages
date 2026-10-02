using AwadyLab.EFCatalyst.Abstractions;

namespace SampleApp.Infrastructure;

/// <summary>
/// The tenant of the current unit of work (in a web app: resolved from the request). As the
/// <see cref="ICurrentTenantAccessor{TTenantKey}"/> it drives the tenant filter and the tenant guard.
/// </summary>
public sealed class TenantSession : ICurrentTenantAccessor<int>
{
    /// <summary>Gets or sets the tenant; 0 means a host/administrator context that is not restricted.</summary>
    public int TenantId { get; set; }
}

/// <summary>The acting user, read by the audit step and by the audit log.</summary>
public sealed class CurrentUser : ICurrentUserAccessor
{
    public string? UserId { get; set; }
}
