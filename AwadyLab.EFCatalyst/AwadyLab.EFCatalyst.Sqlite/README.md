# AwadyLab.EFCatalyst.Sqlite

SQLite support for [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst): sortable
`DateTimeOffset` storage, busy/locked detection and SQLite SQL for the EFCatalyst migration operations.

![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

The package registers, in EF Core's internal service provider, a SQLite dialect (busy/locked detection,
seed-history DDL) and a migrations SQL generator that translates the EFCatalyst migration operations and delegates
everything else to the SQLite provider's own generator.

### Key Highlights

* **Sortable `DateTimeOffset`**: values are stored as a sortable integer, so they order and compare correctly in
  SQL. On by default (`StoreDateTimeOffsetAsBinary`).
* **Concurrency**: the database does not maintain `IRowVersion`, so the save pipeline's concurrency step rotates the
  token; a conflict throws `EFCatalystConcurrencyException`.
* **Busy/Locked Detection**: `SQLITE_BUSY` and `SQLITE_LOCKED` are recognised, so `DeadlockRetryInterceptor` can retry the
  command (outside transactions).
* **Migration Helpers in SQLite SQL**: views, upserts, soft-delete and timestamp triggers, and filtered indexes.

> [!NOTE]
> SQLite ignores schemas (as EF Core does) and has no SQL-defined functions: `CreateOrReplaceFunction` and
> `DropFunctionIfExists` throw `NotSupportedException`. Register a .NET function on the connection
> (`SqliteConnection.CreateFunction`) and map it with `HasDbFunction` instead.

---

## Installation

```bash
dotnet add package AwadyLab.EFCatalyst.Sqlite
```

---

## Quickstart

```csharp
services.AddDbContext<ShopContext>((sp, o) => o
    .UseSqlite(connectionString)
    .UseEFCatalystSqlite()                       // or UseEFCatalystSqlite(s => s.StoreDateTimeOffsetAsBinary = false)
    .UseEFCatalystInterceptors(sp));
```

> [!IMPORTANT]
> If you replace `IMigrationsSqlGenerator` yourself, derive from the EFCatalyst one or call
> `UseEFCatalystSqlite()` after your replacement.

---

## License

Proprietary — see the core package, [AwadyLab.EFCatalyst](https://www.nuget.org/packages/AwadyLab.EFCatalyst).
Full documentation lives in the core package's repository (`README.md`).
