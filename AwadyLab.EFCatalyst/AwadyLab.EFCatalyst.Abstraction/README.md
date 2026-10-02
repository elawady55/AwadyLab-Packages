# AwadyLab.EFCatalyst.Abstraction

Contracts and entity bases for [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst), for
domain projects that should not depend on the full toolkit.

![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

A domain project references this package to declare what its entities are (auditable, soft-deletable,
multi-tenant, versioned) and which services the application provides. The behaviour behind those contracts lives
in `AwadyLab.EFCatalyst`, referenced only by the project that owns the `DbContext`.

### Key Highlights

* **Entity Interfaces**: `IEntity<TKey>`, `ISoftDelete`, `ICreationAudited`, `IModificationAudited`,
  `IDeletionAudited`, `IAuditEntity`, `IFullAudited`, `ITenantEntity<T>`, `IRowVersion`, `IActiveState`,
  `IHashableEntity`, `IHasDomainEvents`.
* **Entity Bases**: `Entity`, `AuditedEntity`, `SoftDeleteEntity`, `DeletionAuditedEntity`, `FullAuditedEntity`,
  `TenantEntity`, `AggregateRoot`, `FullAuditedAggregateRoot`.
* **Service Abstractions**: `ICurrentUserAccessor`, `ICurrentTenantAccessor`, `IDomainEventDispatcher`,
  `IColumnEncryptionKeyProvider`, implemented by the application.
* **Extension Points**: `IDomainEvent`, `IGlobalFilter<T>` and `IMigrationSeeder`.
* **Shared Models and Exceptions**: `PagedResult`, `CursorPage`, `HierarchyItem`, `EntityValidationError`, and the
  `EFCatalystConcurrencyException`, `EFCatalystValidationException`, `IntegrityViolationException` and
  `TenantViolationException` a caller can catch without referencing the toolkit.

---

## Installation

```bash
dotnet add package AwadyLab.EFCatalyst.Abstraction
```

---

## Quickstart

```csharp
public sealed class Order : FullAuditedAggregateRoot<int>, ITenantEntity<int>
{
    public decimal Total { get; private set; }
    public int TenantId { get; private set; }
    public void Place() => AddDomainEvent(new OrderPlaced(Id));
}
```

The entity types and their behaviour are described in the "Domain Model" section of the
[AwadyLab.EFCatalyst README](https://github.com/elawady55/AwadyLab-Packages/blob/main/AwadyLab.EFCatalyst/README.md).

---

## License

Proprietary — see the core package, [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst).
Full documentation lives in the core package's repository (`README.md`).
