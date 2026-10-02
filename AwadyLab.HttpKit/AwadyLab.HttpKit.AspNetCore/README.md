# AwadyLab.HttpKit.AspNetCore

ASP.NET Core integration for [AwadyLab.HttpKit](https://www.nuget.org/packages/AwadyLab.HttpKit): header
propagation, the tenant, region and caller identity from `HttpContext`, and circuit-breaker health checks.

![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

### Key Highlights

* **Header Propagation Without Smuggling**: headers captured from the incoming request ride along on every
  HttpKit call, and pass the client's header policy like any other header — a propagated header that violates
  it is rejected, not smuggled through.
* **Multi-Tenancy Without Plumbing**: the tenant and region of the current request drive tenant-mapped base
  URLs, per-tenant authentication and tenant-isolated cache keys, without passing a tenant on every call.
* **Per-User Cache Isolation**: the caller's identity goes into cache keys, so a cached response is never
  served to a different user.
* **Dependency Health**: circuit-breaker state becomes health-check state, per client if you want it.

---

## Installation

```bash
dotnet add package AwadyLab.HttpKit.AspNetCore
```

---

## Quickstart

```csharp
using AwadyLab.HttpKit.AspNetCore;

var httpKit = builder.Services.AddHttpKit(options => { /* clients, defaults, auth */ });

httpKit.AddAspNetCore(propagation =>
{
    propagation.Headers.Add("X-Correlation-Id");
    propagation.Headers.Add("X-Request-Id");
});
builder.Services.AddHealthChecks().AddHttpKitHealthChecks();

var app = builder.Build();
app.UseHeaderPropagation();     // captures the headers of each incoming request
app.MapHealthChecks("/health");
```

`AddAspNetCore` is shorthand for the four registrations below; call them one by one to pick only some.

| Registration | What it does |
| :--- | :--- |
| `AddHttpKitHeaderPropagation(configure?)` | header propagation |
| `AddHttpContextTenant(configure?)` | the tenant of the current request (`ITenantAccessor`) |
| `AddHttpContextRegion(configure?)` | the region of the current request (`IRegionAccessor`) |
| `AddHttpContextAuthContext()` | the caller's identity for cache keys (`IAuthContextAccessor`) |

---

## 1. Header Propagation

```csharp
httpKit.AddHttpKitHeaderPropagation(propagation =>
{
    propagation.Headers.Add("X-Correlation-Id");
    propagation.Headers.Add("X-Tenant-Id", "X-Upstream-Tenant");   // inbound name → outbound name
});

app.UseHeaderPropagation();
```

Configuration is ASP.NET Core's own `HeaderPropagationOptions`, so renaming a header or computing a value
works as documented there. The middleware captures the headers; HttpKit adds them to every HttpKit request:

* **The caller wins**: a header the request already carries is never overwritten; propagation only fills in
  what is missing.
* **Policed**: propagation runs before header sanitization, so a propagated value with a CR/LF, or a header the
  client forbids, is rejected like any other.
* **Harmless outside a request**: a background job or startup probe has nothing captured, and nothing is added.
* **No middleware, no propagation**: without `app.UseHeaderPropagation()` nothing is captured — not an error.

---

## 2. Tenant, Region & Identity

### Tenant (`AddHttpContextTenant`)

```csharp
using AwadyLab.HttpKit.AspNetCore.Routing;

httpKit.AddHttpContextTenant(tenant =>
{
    tenant.Sources.Clear();
    tenant.Sources.Add(TenantSource.Header);
    tenant.Sources.Add(TenantSource.HostPrefix);
    tenant.HeaderName = "X-Tenant";
});
```

Sources are tried in order; the first that yields a value wins. (This `TenantSource` lives in
`AwadyLab.HttpKit.AspNetCore.Routing`; the core package's `AwadyLab.HttpKit.Options.TenantSource` is a
different setting — whether a *client* takes the tenant from the call or from this accessor.)

| Option | Default | Description |
| :--- | :--- | :--- |
| `Sources` | `Claim`, `Header`, `RouteValue` | also `HostPrefix` — `acme` from `acme.example.com` (a single-label host like `localhost` is none) |
| `ClaimType` | `"tenant"` | |
| `HeaderName` | `"X-Tenant-Id"` | |
| `RouteValueKey` | `"tenant"` | |

The tenant feeds the core package's `Tenants.BaseUrls`, `Tenants.AuthProviders`, per-tenant token caching
(`PerTenant`) and `Caching.IncludeTenant`. A per-call `RequestOptions.Tenant` still takes precedence under the
default `Tenants.Source = RequestThenAmbient`.

> [!IMPORTANT]
> A header or route value is caller-controlled. If the tenant selects credentials or a base URL, prefer a
> claim from an authenticated principal, or validate the header before trusting it.

### Region (`AddHttpContextRegion`)

| Option | Default | Description |
| :--- | :--- | :--- |
| `HeaderName` | `"X-Region"` | read first |
| `ClaimType` | `"region"` | read when the header is absent |

The region feeds `Regions.BaseUrls` and `Regions.AuthProviders`.

### Identity (`AddHttpContextAuthContext`)

For an authenticated user, the subject is the first of the `NameIdentifier`, `sub` and `Name` claims. It is
hashed into the cache key of every client with `Caching.IncludeAuthContext` (on by default), so a cached
response is never served to a different caller. Register it whenever a client caches responses that depend on
who asked.

---

## 3. Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddHttpKitHealthChecks();                                        // one check over every client

builder.Services.AddHealthChecks()
    .AddHttpKitClientHealthChecks(["HttpKit:MyApp.GitHubClient", "HttpKit:MyApp.StripeClient"]);  // one per client
```

| Breaker state | Health |
| :--- | :--- |
| Closed | `Healthy` |
| Half-open | `Degraded` (`Healthy` when `DegradedWhenHalfOpen = false`) |
| Open or isolated | `OpenStatus` — `Unhealthy` by default |

The data payload names each client, its state, when it opened and the failure that tripped it. One check per
client shows *which* dependency is failing instead of one aggregate red light. Both methods also take a
`failureStatus`, `tags` and, for the single check, a `name`. `HttpKitHealthCheckOptions.ClientNames` limits
the single check to some clients.

Client names follow `HttpKit:{FullTypeName}` (`HttpKit:{FullTypeName}#stream` for a streamed client).

---

## License

Proprietary — see the core package, [AwadyLab.HttpKit](https://www.nuget.org/packages/AwadyLab.HttpKit).
Full documentation lives in the core package's repository (`README.md`).
