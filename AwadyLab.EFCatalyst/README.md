# AwadyLab.EFCatalyst

An Entity Framework Core 10 toolkit for the things most line-of-business data layers rebuild by hand: auditable,
soft-deletable and multi-tenant entities, named global query filters, LINQ helpers for filtering, sorting and
paging, a SaveChanges pipeline, column encryption, change-tracking audit logs and migration helpers for views,
triggers, upserts and filtered indexes.

![Tests](https://img.shields.io/badge/tests-passing-brightgreen.svg)
![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Language](https://img.shields.io/badge/C%23-14-blue.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

`AwadyLab.EFCatalyst` plugs into an ordinary `DbContext`: entities implement small interfaces (or derive from the
provided bases), the model picks up the matching conventions and filters, and a set of interceptors does the
auditing, tenancy and soft-delete work on every save. Targets .NET 10 and EF Core 10.

There are five packages, and they ship in lockstep:

| Package | What it adds |
| :--- | :--- |
| `AwadyLab.EFCatalyst.Abstraction` | Contracts and entity bases (entity interfaces and base classes, domain events, global filters, seeders, service abstractions) for domain projects. See [its README](AwadyLab.EFCatalyst.Abstraction/README.md). |
| `AwadyLab.EFCatalyst` | Every provider-neutral feature in this document (references the abstractions). |
| `AwadyLab.EFCatalyst.SqlServer` | Native `rowversion`, deadlock detection, T-SQL for the migration helpers. See [its README](AwadyLab.EFCatalyst.SqlServer/README.md). |
| `AwadyLab.EFCatalyst.PostgreSql` | Deadlock detection, PostgreSQL SQL for the migration helpers. See [its README](AwadyLab.EFCatalyst.PostgreSql/README.md). |
| `AwadyLab.EFCatalyst.Sqlite` | Sortable `DateTimeOffset` storage, busy/locked detection, SQLite SQL for the migration helpers. See [its README](AwadyLab.EFCatalyst.Sqlite/README.md). |

### Key Highlights

* **Entities That Audit Themselves**: Implement `ICreationAudited`, `IModificationAudited`, `IDeletionAudited` or
  `ITenantEntity<T>` (or derive from `FullAuditedAggregateRoot<TKey>`) and the save pipeline stamps the audit
  columns, assigns the tenant and turns deletes into soft deletes.
* **Named Global Filters**: Soft delete and tenant isolation are applied as named query filters, so one query can
  switch one of them off (`IncludeSoftDeleted`, `IgnoreTenantFilter`) without losing the other.
* **One SaveChanges Pipeline**: Audit, tenant guard, soft delete, concurrency, validation, integrity hashing and
  domain events run in a single pass that detects changes once and walks the change set once.
* **Column Security**: `StoreEncrypted` (AES-256-GCM), `StoreMasked` and `StorePermutated`, configured with the
  fluent API only.
* **Migrations Beyond Tables**: Idempotent seed data, upserts, views, functions, filtered and soft-delete indexes,
  and soft-delete and timestamp triggers, generated per provider.
* **Low-Allocation Hot Paths**: Per-entity-type metadata is computed once per model; expression trees and
  reflection are cached; crypto and naming code works on pooled or stack buffers.
* **A Tracked Public API**: Every public symbol is declared in `PublicAPI.*.txt`; everything else is internal.

---

## Installation

```bash
dotnet add package AwadyLab.EFCatalyst
dotnet add package AwadyLab.EFCatalyst.SqlServer     # or .PostgreSql / .Sqlite, next to the EF Core provider
dotnet add package AwadyLab.EFCatalyst.Abstraction   # optional, domain projects that only need the contracts
```

---

## Quickstart

### 1. Define an entity

```csharp
public sealed class Order : FullAuditedAggregateRoot<int>, ITenantEntity<int>
{
    public decimal Total { get; private set; }
    public int TenantId { get; private set; }
    public void Place() => AddDomainEvent(new OrderPlaced(Id));
}
```

### 2. Configure the context

```csharp
public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b) =>
        b.ConfigureDecimalPrecision(18, 2).ConfigureDateTimeUtc();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.AddDefaultQueryFilters(this);            // soft delete + tenant, as named filters
}
```

### 3. Register in DI (`Program.cs`)

```csharp
services.AddEFCatalyst(o => o.Features.Validation = true);
services.AddScoped<ICurrentUserAccessor, HttpUserAccessor>();   // audit user
services.AddScoped<ICurrentTenantAccessor<int>, HttpTenantAccessor>(); // current tenant, read per query
services.AddScoped<IDomainEventDispatcher, MediatorDispatcher>(); // optional
services.AddDbContext<ShopContext>((sp, o) => o
    .UseSqlServer(connectionString)
    .UseEFCatalystSqlServer()
    .UseEFCatalystInterceptors(sp));
```

Removing an `Order` now sets `IsDeleted`, `DeletedAtUtc` and `DeletedBy`; creating and updating stamp the audit
columns; new rows get the context's tenant and writes to another tenant's rows are rejected; the placed event is
dispatched after the save succeeds.

---

## 1. Domain Model

### Contracts

| Interface | Members | Used by |
| --- | --- | --- |
| `IEntity<TKey>` | `TKey Id` | keys, equality |
| `ISoftDelete` | `bool IsDeleted` | soft-delete filter, soft-delete step, `OnlySoftDeleted`, `ExecuteSoftDeleteAsync`, `ExecuteRestoreAsync`, `RemovePermanently`, `Restore` |
| `IDeletionAudited : ISoftDelete` | `DeletedAtUtc`, `DeletedBy` | soft-delete step |
| `ICreationAudited` | `CreatedAtUtc`, `CreatedBy` | audit step (immutable after insert) |
| `IModificationAudited` | `LastModifiedAtUtc`, `LastModifiedBy` | audit step |
| `IAuditEntity` | creation + modification | |
| `IFullAudited` | `IAuditEntity` + `IDeletionAudited` | |
| `ITenantEntity<TTenantKey>` | `TenantId` | tenant filter, tenant guard |
| `IRowVersion` | `byte[] RowVersion` | concurrency |
| `IActiveState` | `bool IsActive` | active-state filter (`IncludeInactive()` to bypass) |
| `IHashableEntity` | `string? IntegrityHash` | integrity step |
| `IHasDomainEvents` / `IDomainEvent` | pending events | domain-events step |

The contracts only declare getters. EFCatalyst writes the values through EF Core, so bases can keep setters
`protected` and the application cannot forge audit data by accident.

### Base classes

`Entity<TKey>` → `AuditedEntity<TKey>` → `FullAuditedEntity<TKey>`; `Entity<TKey>` → `SoftDeleteEntity<TKey>` →
`DeletionAuditedEntity<TKey>`; `TenantEntity<TKey, TTenantKey>`; `AggregateRoot<TKey>` and
`FullAuditedAggregateRoot<TKey>`. Combine a base with extra interfaces when you need several capabilities
(`FullAuditedAggregateRoot<int>, ITenantEntity<int>`).

**Equality**: two entities are equal when they have the same concrete type (EF lazy-loading proxies are unwrapped)
and the same non-default id. Transient entities (default id) are only equal to themselves. The hash code changes when
the id is assigned, so do not keep a new entity in a `HashSet` across the save that generates its key.

**Domain events**: `AggregateRoot.AddDomainEvent` records events in memory (never mapped). After a successful
`SaveChanges` the pipeline passes them, in order, to `IDomainEventDispatcher` and clears them. A failed save
dispatches nothing and keeps the events. Without a registered dispatcher the events stay on the aggregate for you to
handle.

### Multi-tenancy

Tenancy lives on the entities only: an entity implements `ITenantEntity<TTenantKey>` and the context stays a plain
`DbContext`. The current tenant comes from an `ICurrentTenantAccessor<TTenantKey>` registered in DI (scoped is fine,
e.g. read from the HTTP request). `AddDefaultQueryFilters` adds the tenant filter for every tenant key type found on
the entities; the filter resolves the accessor for each query, so one cached model serves all tenants. When no
accessor is registered, or it returns the default value, the filter matches nothing — cross-tenant reads must be
explicit (`IgnoreTenantFilter()`) — and writes are not restricted (a host operation).

---

## 2. Querying

All helpers are extension methods on `IQueryable<T>` in `AwadyLab.EFCatalyst.Extensions`
(class `EFCatalystQueryableExtensions`).

### Conditional composition

```csharp
var query = context.Orders
    .WhereIf(onlyOpen, o => o.Status == Status.Open)
    .WhereIfNotNull(filter.CustomerId, o => o.CustomerId == filter.CustomerId)
    .WhereIfNotNullOrWhiteSpace(filter.Search, o => o.Number.Contains(filter.Search!))
    .WhereIfNotEmpty(filter.Ids, o => filter.Ids!.Contains(o.Id))
    .IncludeIf(withLines, o => o.Lines)
    .AsNoTrackingIf(readOnly)
    .OrderByIf(sortByDate, o => o.CreatedAtUtc);
```

`WhereIf(Func<bool>, ...)` evaluates the delegate once, while the query is composed.

### EXISTS

```csharp
context.Customers.WhereExists(c => c.Orders, o => o.Total > 100);        // EXISTS (SELECT 1 ...)
context.Customers.WhereNotExists(context.Invoices, (c, i) => i.CustomerId == c.Id && !i.Paid);
```

### Large IN lists

`WhereIn(o => o.Id, ids, chunkSize: 1000)` de-duplicates the values and emits parameterised `IN` lists of at most
`chunkSize` values joined with `OR` (`WhereNotIn` uses `AND`), staying below SQL Server's 2,100-parameter limit.
An empty list matches nothing (`WhereNotIn`: everything).

### Dynamic sorting

```csharp
query.OrderByProperty("Customer.Name", descending: true);
query.ApplySorting(request.Sort, allowedProperties: ["Number", "Total", "CreatedAtUtc"]);
```

Specifications look like `"Total desc, Number"` or `"-Total,+Number"`. Paths bind only public readable
properties, case-insensitively, at most four levels deep and five columns; anything else is an `ArgumentException`
(the offending name is echoed truncated). Pass an allow-list for client input so users can only sort by indexed
columns. Resolved selectors are cached per type; only valid paths are cached, so client input cannot grow the cache.
`OrderByIf` / `ThenByIf` sort conditionally.

### Paging

* `PageBy(pageIndex, pageSize)` — zero-based `Skip/Take` with overflow protection.
* `ToPagedListAsync(pageIndex, pageSize)` — items plus total count as `PagedResult<T>`; the count query is skipped
  when the first page is not full.
* `Slice(key, id, cursor, size, options)` / `ToCursorPageAsync(...)` — keyset pagination:
  `WHERE key > k OR (key = k AND id > i) ORDER BY key, id`, so page 1,000 costs the same as page 1. The id must be
  unique. `ToCursorPageAsync` reads one extra row and returns `NextCursor` (null on the last page). Keys must be
  non-nullable; numbers, dates, strings and GUIDs are supported. Set `CursorOptions.SigningKeyProvider` to
  HMAC-sign cursors that travel to untrusted clients.

### Filter switches and bulk soft delete

* `IncludeSoftDeleted()` — bypasses only the soft-delete filter for this query.
* `IncludeInactive()` — bypasses only the active-state filter for this query.
* `OnlySoftDeleted()` — soft-deleted rows only.
* `IgnoreTenantFilter()` — bypasses only the tenant filter and tags the SQL with `EFCatalyst: tenant filter ignored`.
* `IgnoreQueryFilter<TEntity, TInterface>()` / `IgnoreQueryFilter(typeof(TInterface))` — bypass one
  `IGlobalFilter<TInterface>`.
* `IgnoreQueryFiltersIf(condition)` — all filters, conditionally.
* `ExecuteRestoreAsync(restoredBy, timeProvider)` — one `UPDATE` that clears `IsDeleted` (and deletion audit) for the
  matching soft-deleted rows and stamps modification audit. Tracked instances are not refreshed.
* `ExecuteSoftDeleteAsync(deletedBy, timeProvider)` — one `UPDATE` that sets `IsDeleted` and the deletion and
  modification audit for the matching live rows. Use it instead of `ExecuteDeleteAsync`, which bypasses the save
  pipeline and deletes the rows for real.

### Hierarchies (recursive queries)

For self-referencing tables (categories, org charts, folders), pass the key and parent-key properties:

```csharp
var subtree = await db.Categories.GetDescendantsAsync(5, c => c.Id, c => c.ParentId);
var crumbs  = await db.Categories.GetAncestorsAsync(42, c => c.Id, c => c.ParentId,
    new HierarchyQueryOptions<Category> { IncludeSelf = true, Query = q => q.AsNoTracking() });

foreach (var (category, depth) in subtree) { /* depth 1 = child, 2 = grandchild, ... */ }
```

* One recursive CTE per call finds the keys and depths (`WITH` on SQL Server, `WITH RECURSIVE` on PostgreSQL and
  SQLite); the entities are then loaded with a normal EF Core query, so query filters, `Include` (via `Query`) and
  tracking apply. Results are `HierarchyItem<T>(Entity, Depth)`, nearest first.
* Table and column names come from the model (`GetDelimitedTableName` / `GetDelimitedColumnName`), so `ToTable`,
  `HasColumnName` and schemas are honoured; the start key and depth limit are parameters.
* `MaxDepth` (default 100) bounds the recursion and stops cycles; a node reached twice is reported at its nearest
  depth. SQL Server's own 100-level limit is lifted (`OPTION (MAXRECURSION 0)`).
* The recursion walks the table itself: a node hidden by a query filter (soft delete, tenant) is not returned, but
  its children are. Keys must be single, provider-native columns (no value converters). Needs a provider package.

---

## 3. Interceptors

Register once with `services.AddEFCatalyst(options => ...)` and attach with
`options.UseEFCatalystInterceptors(serviceProvider)` inside `AddDbContext`. Interceptors are singletons; per-save
services (`ICurrentUserAccessor`, `IDomainEventDispatcher`, `IColumnEncryptionKeyProvider`, `IMigrationSeeder`) are
resolved from the context's own service scope first, so they may be scoped. Without dependency injection use
`UseEFCatalystInterceptors()` or `UseEFCatalystInterceptors(options => ...)`; interceptor instances are shared per
configuration so EF Core does not build a new internal service provider for every context.

Interceptors are added in this order; a feature switched off in `EFCatalystOptions.Features` is not added at all.
You rarely need the switches: every feature only acts on entities that implement its contract, so an entity opts
in by implementing the interface (`services.AddEFCatalyst(o => o.Features.Validation = true)`).

1. `QueryTaggingInterceptor` (off by default)
2. `DeadlockRetryInterceptor`
3. `SlowQueryLoggingInterceptor`
4. `EFCatalystSaveChangesInterceptor`

### Save pipeline — `EFCatalystSaveChangesInterceptor`

Changes are detected once and the change set is walked once. For each added, modified or deleted entry:

| Step | Option | Behaviour |
| --- | --- | --- |
| Tenant guard | `TenantGuard` | new `ITenantEntity` rows get the context tenant; adding for, modifying or deleting another tenant's row, or moving a row between tenants, throws `TenantViolationException`. Host contexts (no tenant) are not restricted. |
| Soft delete | `SoftDelete` | a deleted `ISoftDelete` entity becomes an update of `IsDeleted` (+ `DeletedAtUtc`/`DeletedBy`); its owned entities are kept. `context.RemovePermanently(entity)` deletes for real; `context.Restore(entity)` clears `IsDeleted`, and saving clears `DeletedAtUtc`/`DeletedBy`. |
| Audit | `Audit` | creation stamps on insert (explicit values are kept), modification stamps on update; creation columns can never be changed afterwards. |
| Concurrency | `Concurrency` | rotates `IRowVersion` tokens where the database does not (PostgreSQL, SQLite); conflicts throw `EFCatalystConcurrencyException` (a `DbUpdateConcurrencyException`) naming entity type and key. |
| Validation | `Validation` (off) | DataAnnotations/`IValidatableObject`; all failures are collected into `EFCatalystValidationException`. |
| Integrity | `Integrity` | recomputes the `IHashableEntity` hash (last, so it covers the stamped values). |

After a successful save the domain events collected from aggregates are dispatched. Every loaded
`IHashableEntity` is verified; a mismatch logs event 41003 by default, or throws `IntegrityViolationException` with
`IntegrityViolationBehavior.Throw`. Rows without a hash are tolerated unless `RequireIntegrityHash = true`.

> [!NOTE]
> EF Core applies cascade deletes/`SET NULL` to tracked dependents when you call `Remove` (default
> `CascadeTiming.Immediate`). For soft deletes set
> `context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges` so cascades are evaluated after the
> pipeline turned the delete into an update.

### Slow queries — `SlowQueryLoggingInterceptor`

Logs event 41001 (warning) with the elapsed time and the command text (truncated to 2,000 characters) for commands
slower than `SlowQueryThreshold` (1000 ms). Parameter values are never logged.

### Query tags — `QueryTaggingInterceptor`

Prefixes commands with `-- EFCatalyst app=… trace=… tenant=… [user=…]` so database monitoring can be joined with
distributed traces. Values are restricted to `[A-Za-z0-9._:@-]` and 64 characters; the user id is opt-in
(`QueryTagIncludesUser`) because it can be personal data.

### Deadlock retry — `DeadlockRetryInterceptor`

Retries commands that fail with a deadlock (SQL Server 1205, PostgreSQL 40P01, SQLite busy/locked) up to
`DeadlockMaxRetries` times with exponential backoff and jitter — but only outside transactions. Inside a
transaction the database already rolled everything back, so replaying one statement would be wrong; use the
provider's retrying execution strategy (`EnableRetryOnFailure`) for whole units of work. When the context uses a
retrying strategy, this interceptor stands aside. Needs a provider package (`UseEFCatalystXxx()`).

---

## 4. Model Building

### Global query filters

EFCatalyst uses EF Core 10 **named** query filters, keyed by the interface a filter targets, so one filter can be
switched off without the others.

```csharp
public sealed class ArchivedFilter : IGlobalFilter<IArchivable>
{
    public Expression<Func<IArchivable, bool>> Build(DbContext context) => e => !e.IsArchived;
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... entity configuration first ...
    modelBuilder.AddDefaultQueryFilters(this);                   // soft delete + active state + tenant
    modelBuilder.AddGlobalQueryFilterInterfacesInAssembly(typeof(ShopContext).Assembly, this);   // your own filters
}
```

A filter is applied to every root entity type implementing its interface (derived types inherit it; owned types
are skipped). Interface members are rebound to the entity's own properties, so the SQL is a plain column
comparison. A filter that needs per-context values must read them from members of the context passed to `Build`
(EF Core then re-reads them for each query), never from captured state.

`AddDefaultQueryFilters` covers the built-in contracts; each filter only touches entities implementing its interface:
`ISoftDelete` (`IncludeSoftDeleted()` to bypass), `IActiveState` (`IncludeInactive()`), `ITenantEntity<T>`
(`IgnoreTenantFilter()`). The built-in filter classes are internal.

Filter keys are `EFCatalystFilterKeys.Prefix` + the interface's full name (prefix `EFCatalyst:` by default). To use
your own prefix, call `EFCatalystFilterKeys.SetPrefix("MyApp:")` once at startup, before any model is built. A
second call, or a call after any key has been read, throws `InvalidOperationException`.

### Configuration bases

`EntityConfigurationBase<TEntity, TKey>` maps the key, then applies a configurator for every contract the entity
implements, then calls the abstract `ConfigureEntity`:

| Contract | Mapping |
| --- | --- |
| `IRowVersion` | `RowVersion` required concurrency token |
| `ITenantEntity<T>` | `TenantId` required and indexed |
| `ICreationAudited` / `IModificationAudited` | `CreatedAtUtc` required; `CreatedBy`, `LastModifiedBy` max 256 |
| `ISoftDelete` / `IDeletionAudited` | `IsDeleted` required, database default `false`; `DeletedBy` max 256 |
| `IActiveState` | `IsActive` required |
| `IHashableEntity` | `IntegrityHash` ASCII, max 128 |
| `IHasDomainEvents` | `DomainEvents` ignored (never mapped) |

`AuditedEntityConfigurationBase<TEntity, TKey>` changes the user-column length by overriding `UserIdMaxLength`.
Column security (`StoreEncrypted`, `StoreMasked`, `StorePermutated`) is configured with the fluent API only.

### Conventions (in `ConfigureConventions`)

| Method | Effect |
| --- | --- |
| `ConfigureDateTimeUtc()` | `DateTime` written as UTC, read back with `DateTimeKind.Utc` |
| `ConfigureDecimalPrecision(18, 4)` | default precision/scale of `decimal` |

Every convention yields to explicit configuration of a property or entity.

---

## 5. Column Security

### Keys

`IColumnEncryptionKeyProvider` supplies 256-bit master keys by id plus the id used for new data.
`ColumnEncryptionKeyRing` is the in-memory implementation (`FromBase64` for configuration). Load keys from a
secret store (Key Vault, environment, user secrets) — never from source control. Every purpose (encryption,
synthetic IV, permutation, integrity, cursors) uses its own HKDF-SHA256 sub-key, so one key id can serve all of them.

### Storage options

| Method | Storage | Reversible | Queryable by equality |
| --- | --- | --- | --- |
| `StoreEncrypted(keys)` | AES-256-GCM, random nonce | yes | no |
| `StoreEncrypted(keys, deterministic: true)` | AES-256-GCM, synthetic IV (HMAC of header + value) | yes | yes |
| `StoreMasked(prefix, suffix, char)` | only the mask (`************1111`) | **no** | on the masked value |
| `StorePermutated(keys)` | keyed format-preserving substitution | yes | yes |

Encrypted values are Base64 of `version | flags | keyId | nonce | tag | ciphertext`; the header is authenticated, so
it cannot be altered or swapped, and old key ids stay readable after rotation while the provider still returns them.
Deterministic encryption reveals which rows share a value — use it only for lookup columns. Permutation keeps
length and character classes and is **obfuscation, not encryption**; it records no key id, so its key must stay
fixed for the life of the data. Size encrypted columns generously (Base64 plus ~40 bytes) or leave them unbounded.

All three mark the property sensitive: its values are redacted from `AuditLogEntry` and never appear in
EFCatalyst exception messages.

### Integrity hashes

`IHashableEntity` rows carry `keyId:HMAC-SHA256` over a canonical encoding of their scalar properties (store-generated
columns excluded). The hash is written on save and verified on every load, with a constant-time comparison. Values
are normalised the way databases round-trip them; still, configure column types that can hold the values you write
(a `decimal(18,2)` column silently rounding 1.234 is reported as tampering).

---

## 6. Shadow Properties & Change Tracking

### Shadow properties

```csharp
builder.Entity<Tag>().Property<DateTimeOffset>("ImportedAtUtc");      // your own shadow properties
var imported = context.Entry(tag).GetShadowProperty<DateTimeOffset>("ImportedAtUtc");
context.SetShadowProperty(tag, "Priority", 3);
var mine = context.Tags.WhereShadowEquals("ImportedBy", userId);   // parameterised
```

Accessors verify that the property exists, is a shadow property and has a compatible type; required shadow
properties cannot be set to null.

### Inspecting entries

* `entry.GetChangedProperties()` — modified properties (original and current values); all properties for added
  (current) and deleted (original) entries.
* `entry.HasPropertyChanged(e => e.Email)` — modified in this unit of work (or the entity is new).
* `entry.GetOriginalValue(e => e.Email)` / `entry.GetOriginalValue<string>("Email")`.

### Audit log

```csharp
var entries = context.ChangeTracker.ToAuditEntryList();   // user and clock from DI; or pass them explicitly
await context.SaveChangesAsync();
entries.ResolveTemporaryKeys();          // pick up database-generated keys
logger.LogInformation("{Audit}", JsonSerializer.Serialize(entries));
```

Each `AuditLogEntry` has the entity and table name, the action (`Created`, `Updated`, `Deleted`, `SoftDeleted`,
`Restored` — a flip of `IsDeleted` is reported as such), key values, property diffs, timestamp, user and tenant.
Values of sensitive (encrypted, masked, permutated) properties are replaced with `[redacted]`. The list is captured
before the save pipeline stamps audit columns; capture after `SaveChanges` is not possible because the entries are
then unchanged.

---

## 7. Schema & Migrations

### Schema reflection

```csharp
context.GetTableName<Order>();                     // "sales.Orders" (unquoted; for diagnostics/tooling)
context.GetColumnName<Order>(o => o.CustomerId);   // column in the entity's table
context.GetColumnName<Order>("TenantId");          // shadow properties too
context.GetPrimaryKeys<Order>();                   // key column names in key order
context.GetDelimitedTableName<Order>();            // [sales].[Orders] / "sales"."Orders": quoted for raw SQL
context.GetDelimitedColumnName<Order>(o => o.CustomerId);
context.GetMappedProperties<Order>();              // name, column, CLR type, store type, nullability, key, shadow
```

### Maintenance

* `EnsureDatabaseDeletedAndRecreatedAsync(seed)` — drops the database, clears the change tracker, applies all
  migrations (or `EnsureCreated` when there are none), runs configured seeding, then the optional delegate.
  **Deletes all data**; meant for integration tests.
* `HasPendingMigrationsAsync()` / `HasPendingMigrations()` — migrations defined but not applied; suitable for a
  readiness check. Use `Database.HasPendingModelChanges()` to catch model changes without a migration.

### Seeding with `IMigrationSeeder`

```csharp
public sealed class CountrySeeder : IMigrationSeeder
{
    public string Id => "reference/countries";
    public int Order => 0;
    public string? AfterMigration => "20260101120000_AddCountries";
    public async Task SeedAsync(DbContext context, CancellationToken ct) => context.Add(new Country("NO", "Norway"));
}

services.AddSingleton<IMigrationSeeder, CountrySeeder>();
services.AddDbContext<ShopContext>((sp, o) => o.UseSqlServer(cs).UseEFCatalystSqlServer().UseEFCatalystSeeding(sp));
await context.Database.MigrateAsync();             // runs pending seeders after the migrations
```

Seeders run through EF Core's `UseAsyncSeeding`/`UseSeeding` hooks (so `Migrate` and `EnsureCreated` trigger them),
in `Order`, each in its own transaction together with its row in `__EFCatalystSeedHistory`. A seeder runs once per
database; one waiting for its `AfterMigration` is retried on the next run; a failing one leaves no data behind.

### Migration helpers

Available on `MigrationBuilder` once the provider package is enabled (without it EF Core reports the unknown
operation by name):

| Helper | SQL Server | PostgreSQL | SQLite |
| --- | --- | --- | --- |
| `InsertDataIfMissing` | `IF NOT EXISTS … INSERT` | `ON CONFLICT DO NOTHING` | `ON CONFLICT DO NOTHING` |
| `UpsertData` | `MERGE … WITH (HOLDLOCK)` | `ON CONFLICT DO UPDATE` | `ON CONFLICT DO UPDATE` |
| `DeleteDataIfExists` | skips a missing table | skips a missing table | plain delete |
| `CreateOrReplaceView` / `DropViewIfExists` | `CREATE OR ALTER VIEW` | `CREATE OR REPLACE VIEW` | drop + create |
| `CreateOrReplaceFunction` / `DropFunctionIfExists` | `CREATE OR ALTER FUNCTION` | `CREATE OR REPLACE FUNCTION` | not supported |
| `CreateFilteredIndex` | filtered index | partial index | partial index |
| `CreateSoftDeleteIndex` | `(IsDeleted, TenantId)` | same | same |
| `AddSoftDeleteTrigger` / `Drop…` | `INSTEAD OF DELETE` | `BEFORE DELETE` returning NULL | `BEFORE DELETE` + `RAISE(IGNORE)` |
| `AddUpdatedTimestampTrigger` / `Drop…` | `AFTER UPDATE`, nest-level guard | `BEFORE UPDATE` | `AFTER UPDATE` |

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.UpsertData("Countries", ["Code"], ["Code", "Name"], new object?[,] { { "NO", "Norway" } });
    migrationBuilder.CreateFilteredIndex("UX_Users_Email", "Users", ["Email"], unique: true, whereNotDeleted: true);
    migrationBuilder.AddSoftDeleteTrigger("Orders", deletedAtColumn: "DeletedAtUtc");
}
```

* The unique index of `UpsertData`/`InsertDataIfMissing` conflict targets must exist (normally the primary key).
  PostgreSQL checks NOT NULL columns before resolving the conflict, so upserts must include every required column.
* On SQL Server, tell EF Core about triggers (`ToTable(t => t.HasTrigger("TR_Orders_SoftDelete"))`) so it does not use
  `OUTPUT` clauses for that table, and do not insert into `rowversion` columns.
* SQLite stores timestamps in a format chosen by the value converter, so its triggers need an explicit
  `deletedAtValueSql`/`valueSql`.
* Timestamp triggers default to the current UTC time (`SYSUTCDATETIME()`, `now()`).

---

## 8. Providers

Enable a provider package next to the EF Core provider:

```csharp
options.UseSqlServer(cs).UseEFCatalystSqlServer();
options.UseNpgsql(cs).UseEFCatalystPostgreSql();
options.UseSqlite(cs).UseEFCatalystSqlite();                  // or UseEFCatalystSqlite(o => ...)
```

Each registers, in EF Core's internal service provider, a dialect (deadlock detection, seed-history DDL) and a
migrations SQL generator that translates the EFCatalyst migration operations and delegates everything else to the
provider's own generator.

> [!IMPORTANT]
> If you replace `IMigrationsSqlGenerator` yourself, derive from the EFCatalyst one or call the provider package
> after your replacement.

| | SQL Server | PostgreSQL | SQLite |
| --- | --- | --- | --- |
| `IRowVersion` | native `rowversion` | rotated by EFCatalyst | rotated by EFCatalyst |
| Deadlock detection | error 1205 | SQLSTATE 40P01 | `SQLITE_BUSY` / `SQLITE_LOCKED` |
| `DateTimeOffset` | native | `timestamptz` | stored as sortable integer (`StoreDateTimeOffsetAsBinary`, default on) |
| SQL functions | yes | yes | no |

SQLite ignores schemas (as EF Core does).

---

## 9. Performance & Security

* **Performance**: per-entity-type metadata is computed once per model (runtime annotations); the save pipeline
  detects changes once and walks the change set once; expression trees and reflection are cached; crypto and
  naming code works on pooled or stack buffers. `AwadyLab.EFCatalyst.Benchmarks` compares the pipeline with plain EF.
* **Security**:
  * Migration helpers quote identifiers with the provider and write values as provider-typed literals. View
    bodies, function definitions, index filters and timestamp expressions are developer-written SQL and are emitted
    verbatim — never build them from user input. PostgreSQL bodies use a dollar-quote tag that is guaranteed not to
    occur inside.
  * `ApplySorting`/`OrderByProperty` bind only public readable properties, with depth and column limits, and accept
    an allow-list.
  * Keyset cursors are validated strictly (size, format, required fields); sign them with
    `CursorOptions.SigningKeyProvider` when they cross a trust boundary.
  * Sensitive columns are redacted from audit entries and never appear in exception messages.
  * Query tags are restricted to a safe character set; the user id is opt-in.
  * `IgnoreTenantFilter()` tags its SQL so cross-tenant reads are visible in database logs; the tenant guard rejects
    cross-tenant writes from tenant contexts.

---

## Sample App

`SampleApp` is organised like an application (`Domain/`, `Data/` with configurations, filters and seeders,
`Infrastructure/` with the composition root in `SampleHost`) and runs one scenario per feature area, each on a
freshly seeded database: `model`, `tenancy`, `auditing`, `soft-delete`, `security`, `integrity`, `concurrency`,
`validation`, `events`, `change-tracking`, `querying`, `hierarchy`, `seeding`, `migrations`, `diagnostics`.

```powershell
dotnet run --project SampleApp                      # guided tour on SQLite, every scenario
dotnet run --project SampleApp -- tenancy security  # selected scenarios
```

---

## Building

```powershell
./build.ps1          # build, unit tests, coverage gate (eng/coverage.json) and report in ../coverage-report
./build.ps1 -Pack    # also pack and verify the five packages
dotnet test AwadyLab.EFCatalyst.Tests.Integration   # SQL Server + PostgreSQL containers (Docker)
```

---

## License

Package by Mohamed Elawady

```
Copyright (c) Mohamed Elawady. All rights reserved.

This software is closed source and proprietary. Subject to the terms below,
you are granted a free, worldwide, non-exclusive license to:

  - Use this software, in source or compiled (NuGet package) form, in your
    own applications, including commercial applications, at no charge.

You may NOT:

  - Redistribute, sublicense, sell, or publish this software (or any
    modified version of it) as a standalone product, library, or package,
    including republishing it under a different name on NuGet.org or any
    other package repository.
  - Reverse engineer, decompile, or disassemble the compiled package except
    to the extent applicable law expressly permits this despite this
    limitation.
  - Remove or alter any copyright, trademark, or other proprietary notices.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
