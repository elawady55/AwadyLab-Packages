# AwadyLab.EFCatalyst.PostgreSql

PostgreSQL (Npgsql) support for [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst):
deadlock detection and PostgreSQL SQL for the EFCatalyst migration operations.

![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

The package registers, in EF Core's internal service provider, a PostgreSQL dialect (deadlock detection,
seed-history DDL) and a migrations SQL generator that translates the EFCatalyst migration operations and delegates
everything else to the Npgsql provider's own generator.

### Key Highlights

* **Concurrency**: the database does not maintain `IRowVersion`, so the save pipeline's concurrency step rotates the
  token; a conflict throws `EFCatalystConcurrencyException`.
* **Deadlock Detection**: SQLSTATE 40P01 is recognised, so `DeadlockRetryInterceptor` can retry the command (outside
  transactions).
* **Migration Helpers in PostgreSQL SQL**: views, functions, `ON CONFLICT` upserts, soft-delete and timestamp
  triggers, and filtered indexes.

---

## Installation

```bash
dotnet add package AwadyLab.EFCatalyst.PostgreSql
```

---

## Quickstart

```csharp
services.AddDbContext<ShopContext>((sp, o) => o
    .UseNpgsql(connectionString)
    .UseEFCatalystPostgreSql()
    .UseEFCatalystInterceptors(sp));
```

> [!IMPORTANT]
> If you replace `IMigrationsSqlGenerator` yourself, derive from the EFCatalyst one or call
> `UseEFCatalystPostgreSql()` after your replacement.

---

## License

Proprietary — see the core package, [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst).
Full documentation lives in the core package's repository (`README.md`).
