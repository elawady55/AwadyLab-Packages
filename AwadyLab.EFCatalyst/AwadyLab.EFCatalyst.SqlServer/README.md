# AwadyLab.EFCatalyst.SqlServer

SQL Server support for [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst): native
`rowversion` concurrency, deadlock detection and T-SQL for the EFCatalyst migration operations.

![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

The package registers, in EF Core's internal service provider, a SQL Server dialect (deadlock detection,
seed-history DDL) and a migrations SQL generator that translates the EFCatalyst migration operations and delegates
everything else to the SQL Server provider's own generator.

### Key Highlights

* **Native Concurrency**: `IRowVersion` maps to SQL Server's `rowversion`.
* **Deadlock Detection**: error 1205 is recognised, so `DeadlockRetryInterceptor` can retry the command (outside
  transactions).
* **Migration Helpers in T-SQL**: views, functions, upserts, soft-delete and timestamp triggers, and filtered
  indexes.

---

## Installation

```bash
dotnet add package AwadyLab.EFCatalyst.SqlServer
```

---

## Quickstart

```csharp
services.AddDbContext<ShopContext>((sp, o) => o
    .UseSqlServer(connectionString)
    .UseEFCatalystSqlServer()
    .UseEFCatalystInterceptors(sp));
```

> [!IMPORTANT]
> If you replace `IMigrationsSqlGenerator` yourself, derive from the EFCatalyst one or call
> `UseEFCatalystSqlServer()` after your replacement.

---

## License

Proprietary — see the core package, [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst).
Full documentation lives in the core package's repository (`README.md`).
