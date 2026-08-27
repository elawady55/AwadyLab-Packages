# AwadyLab.ConfigurationUtilities

A .NET package that extends the built-in Options pattern (`IOptions<T>`) with the things real
applications end up needing: validation (three different ways), environment-variable overrides,
encrypted secrets, polymorphic (discriminated-union) config sections, an integrity signature check,
custom remote configuration sources, and bulk registration — all wired together as one ordered
pipeline per settings class, instead of you gluing `ConfigurationBinder`, `OptionsBuilder`, and
`IPostConfigureOptions` together by hand every time.

## Table of contents

- [Installation](#installation)
- [The core idea](#the-core-idea)
- [Feature: Bulk registration](#feature-bulk-registration)
- [Feature: Environment-variable overrides](#feature-environment-variable-overrides)
- [Feature: Forbidden-in-Production values](#feature-forbidden-in-production-values)
- [Feature: Validation (three ways)](#feature-validation-three-ways)
  - [1. DataAnnotations](#1-dataannotations)
  - [2. Custom validation](#2-custom-validation)
  - [3. FluentValidation](#3-fluentvalidation)
- [Feature: Encrypted settings](#feature-encrypted-settings)
  - [1. Producing the ciphertext](#1-producing-the-ciphertext)
  - [2. Registering keys and marking properties](#2-registering-keys-and-marking-properties)
  - [3. Changing the encryption algorithm](#3-changing-the-encryption-algorithm)
- [Feature: Polymorphic (discriminated-union) binding](#feature-polymorphic-discriminated-union-binding)
  - [1. A single polymorphic object](#1-a-single-polymorphic-object)
  - [2. A collection of polymorphic objects](#2-a-collection-of-polymorphic-objects)
- [Feature: Integrity signature verification](#feature-integrity-signature-verification)
  - [Changing the verification algorithm/provider](#changing-the-verification-algorithmprovider)
- [Feature: Custom configuration sources](#feature-custom-configuration-sources)
  - [Beyond feature flags: key rotation and rotating integrity signatures](#beyond-feature-flags-key-rotation-and-rotating-integrity-signatures)
    - [Key rotation](#key-rotation)
    - [Rotating integrity signatures and certificates](#rotating-integrity-signatures-and-certificates)
- [Custom pipeline steps](#custom-pipeline-steps)
- [Error handling](#error-handling)
- [License](#license)

## Installation

```bash
dotnet add package AwadyLab.ConfigurationUtilities
```

## The core idea

Every settings class implements `IAppSettings`:

```csharp
public interface IAppSettings
{
    static abstract string Key { get; }
}
```

`Key` is the configuration section name. You register it with `AddOptions<T>()`:

```csharp
public class DatabaseSettings : IAppSettings
{
    public static string Key => "Database";

    public string ConnectionString { get; set; } = string.Empty;
    public int MaxPoolSize { get; set; }
}
```

```csharp
var builder = WebApplication.CreateBuilder(args);

var db = builder.AddOptions<DatabaseSettings>(); // bound, validated, returned immediately
// ... later, anywhere via DI:
// IOptions<DatabaseSettings> / IOptionsMonitor<DatabaseSettings>
```

`AddOptions<T>()` throws immediately if the `"Database"` section doesn't exist in configuration —
missing config fails fast at startup, not with a null-reference three services downstream.

Every feature below is a **pipeline step** that `AddOptions<T>()` runs, in a fixed order, against
the bound model. The default pipeline (always active, no opt-in needed) is:

```
Polymorphic binding (objects) → Bind → Polymorphic binding (collections) →
Encryption → Environment-variable overrides → Forbidden-in-Production check →
DataAnnotations validation → Custom validation → FluentValidation
```

You don't normally need to know this order exists — it's simply the order that makes each feature
work correctly relative to the others (e.g. environment variables override config values, so they
have to run after binding; forbidden-value checks run after everything that could change a value).
It matters mainly for the one thing you can tune: **which of these steps get extra configuration**,
via the optional callback:

```csharp
builder.AddOptions<DatabaseSettings>(pipeline => pipeline
    .UseDataAnnotations(o => o.ValidateOnStart = true)
    .UseFluentValidation(o => o.RequireValidator = true));
```

The callback's `pipeline` parameter only exposes the steps that actually take options —
`UseDataAnnotations`, `UseFluentValidation`, `UseIntegritySignature` — plus `Use<T>(...)` for
adding your own custom step (see [Custom pipeline steps](#custom-pipeline-steps)). Binding,
encryption, environment overrides, and polymorphic resolution always run; there's nothing to tune
about *whether* they run, only about what attributes you put on your model to opt individual
properties into them.

---

## Feature: Bulk registration

**Why.** A real application can have a dozen settings classes. Calling `AddOptions<T>()` once per
type is repetitive, and it's easy to forget one.

**When.** Whenever you have more than a couple of `IAppSettings` classes, or want new ones to be
picked up automatically without touching `Program.cs` again.

**How.**

```csharp
using System.Reflection;

builder.AddOptionsInAssembly(Assembly.GetExecutingAssembly());
```

This scans the assembly for every non-abstract class implementing `IAppSettings` (each needs a
parameterless constructor) and calls `AddOptions<T>()` for each one.

A shared `configure` callback applies to *every* type found — useful for a cross-cutting tweak like
turning on `ValidateOnStart` everywhere:

```csharp
builder.AddOptionsInAssembly(Assembly.GetExecutingAssembly(),
    configure: pipeline => pipeline.UseDataAnnotations(o => o.ValidateOnStart = true));
```

If one type needs configuration that doesn't make sense for the others (a specific
`UseIntegritySignature` certificate, for instance), register it individually and exclude it from
the bulk scan so it isn't registered twice:

```csharp
builder.AddOptionsInAssembly(Assembly.GetExecutingAssembly(),
    exclude: [typeof(LicenseSettings)]);

builder.AddOptions<LicenseSettings>(pipeline => pipeline.UseIntegritySignature(new IntegritySignatureOptions
{
    SignatureProvider = () => File.ReadAllBytes("license.sig"),
    CertificateProvider = () => new X509Certificate2("license.cer"),
}));
```

---

## Feature: Environment-variable overrides

**Why.** The twelve-factor pattern: config files hold defaults, environment variables hold
per-deployment or secret overrides, without needing a different appsettings file per environment.

**When.** Any value you want overridable at deploy time without editing JSON — hostnames, feature
flags, anything that varies between a developer's machine, CI, and production.

**How.**

```csharp
using AwadyLab.ConfigurationUtilities.EnvironmentVariables;

public class ApiSettings : IAppSettings
{
    public static string Key => "Api";

    public string BaseUrl { get; set; } = string.Empty;

    [EnvironmentVariable("API_KEY")]
    public string ApiKey { get; set; } = string.Empty;
}
```

If `API_KEY` is set in the environment, it overwrites whatever `appsettings.json` provided —
both on the eagerly-returned model from `AddOptions<T>()` and on every later
`IOptions<T>`/`IOptionsMonitor<T>` resolution. If the variable isn't set, the configured value is
left alone.

---

## Feature: Forbidden-in-Production values

**Why.** Some values are fine for local development but must never reach production — a verbose
log level, an "allow insecure connections" flag, a debug endpoint toggle. Catching that manually
means remembering to check it somewhere; this makes it declarative and enforced at startup.

**When.** Any property where a specific value (or set of values) should hard-fail startup, but
*only* in the Production environment.

**How.**

```csharp
using AwadyLab.ConfigurationUtilities.ForbiddenInProduction;

public class DebugSettings : IAppSettings
{
    public static string Key => "Debug";

    [ForbiddenInProductionWhen(true)]
    public bool AllowInsecureSocket { get; set; }
}
```

If `AllowInsecureSocket` is `true` and `IHostEnvironment.EnvironmentName == "Production"`,
`AddOptions<T>()` throws immediately with a clear message naming the property and its value.
Outside Production, it's a no-op. The check runs after binding, environment overrides, and
decryption, so it always sees the final value — not a stale one.

---

## Feature: Validation (three ways)

There are three independent validation layers. You can use any combination on the same class; all
apply automatically once you register the model (DataAnnotations and custom validation) or once you
also register a FluentValidation validator in DI.

### 1. DataAnnotations

**Why.** The lowest-ceremony option — reuse the attributes you already know
(`[Required]`, `[Range]`, `[EmailAddress]`, ...) instead of writing validation code.

**When.** Simple, single-property constraints.

**How.**

```csharp
using System.ComponentModel.DataAnnotations;

public class EmailSettings : IAppSettings
{
    public static string Key => "Email";

    [Required]
    public string SmtpHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; }
}
```

This is always on for every registered type — no attribute or opt-in call needed beyond `[Required]`
etc. on the properties themselves. It fails on first access to `IOptions<T>.Value` (or eagerly at
host startup, since `ValidateOnStart` defaults to `true`) with an `OptionsValidationException`.

```csharp
// Tune it: validate lazily instead of eagerly at startup
builder.AddOptions<EmailSettings>(pipeline => pipeline.UseDataAnnotations(o => o.ValidateOnStart = false));
```

### 2. Custom validation

**Why.** For rules DataAnnotations can't express — cross-property checks, business logic, anything
needing a real `if` statement.

**When.** A handful of properties need to be validated together, or the rule doesn't map to an
existing attribute.

**How.**

```csharp
using System.ComponentModel.DataAnnotations;
using AwadyLab.ConfigurationUtilities.Validators.CustomValidator;

public class CertificateSettings : IAppSettingValidator // implies IAppSettings
{
    public static string Key => "Certificate";

    public string TTL { get; set; } = "720h";

    public ValidationResult Validate() =>
        TTL.EndsWith('h') && int.TryParse(TTL[..^1], out var hours) && hours > 0
            ? ValidationResult.Success! // null is the idiomatic "valid" — this library handles it correctly
            : new ValidationResult($"'{TTL}' must look like '720h'.");
}
```

This runs **eagerly, synchronously, inside `AddOptions<T>()` itself** — before the host is even
built. That makes it the fastest-failing of the three validation layers: if custom validation and
DataAnnotations/FluentValidation would all fail on the same misconfigured value, the custom
validator's exception is the one you'll see, since it runs first.

### 3. FluentValidation

**Why.** For rich, composable, testable validation rules — especially when you already use
FluentValidation elsewhere in the app.

**When.** Complex rule sets, or you want validators to be independently unit-testable.

**How.**

```csharp
using FluentValidation;

public class DatabaseSettingsValidator : AbstractValidator<DatabaseSettings>
{
    public DatabaseSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.MaxPoolSize).InclusiveBetween(1, 1000);
    }
}
```

```csharp
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.AddOptions<DatabaseSettings>();
```

This package never references FluentValidation at compile time — it detects the package via
reflection, so it stays an optional dependency. If FluentValidation isn't installed, or no
`IValidator<T>` is registered for a type, the check is silently skipped by default. Make it required
instead:

```csharp
builder.AddOptions<DatabaseSettings>(pipeline => pipeline.UseFluentValidation(o => o.RequireValidator = true));
```

> ℹ️ **Note:** FluentValidation runs once the host starts, via a single shared hosted service across
> every type using it — registering FluentValidation (or `UseIntegritySignature`, below) on ten
> config types doesn't mean ten separate `IHostedService`s slowing down startup; they're aggregated
> into one.

> ⚠️ **Gotcha:** if you accidentally register two `IValidator<T>` implementations for the *same*
> settings type, only the **last one registered** with DI is ever consulted (`GetService`, not
> `GetServices`) — the earlier one is silently never run. This is a real DI resolution behavior, not
> a bug in this package; just be aware of it if you see a validator "not firing."

---

## Feature: Encrypted settings

**Why.** Some config values (API keys, connection-string passwords) shouldn't sit in plaintext in
`appsettings.json` or a repo. This lets you store ciphertext instead and decrypt it transparently as
part of binding.

**When.** Any secret that has to live in configuration at all — as opposed to being fetched fully
out-of-band (for that, see [custom configuration sources](#feature-custom-configuration-sources)).

### 1. Producing the ciphertext

You never hand-write ciphertext — a script shipped with the package,
`Encryption/Scripts/Encrypt-AppSetting.ps1`, produces the exact layout the built-in decryptor expects
(`base64(nonce || tag || ciphertext)`, AES-GCM). It has two input modes, matching the two ways
`[AppSettingsEncryption]` can be applied (see part 2 below). Both modes do the exact same encryption —
they only differ in *where the plaintext comes from*.

**Mode A — `-Value`: encrypt a value you type or pipe in directly.** The value can be a plain string
(for a **property-level** attribute) or a JSON document (for a **class-level** attribute, typed/piped
in rather than read from a file).

```powershell
PS> ./Encrypt-AppSetting.ps1 -Value "hunter2" -Key $base64Key
Cipher text: 3q2+7wABAgMEBQYHCAkKC2j8k7X9mPz1nOe4wA==
Key: <the key you passed in>
```

That one output line, `Cipher text: ...`, is what goes into `appsettings.json` in place of the
plaintext value (the actual string will be different every time you run this — AES-GCM uses a
random nonce, so don't expect to reproduce the example value shown here):

```json
{ "ApiSecrets": { "ApiKey": "3q2+7wABAgMEBQYHCAkKC2j8k7X9mPz1nOe4wA==" } }
```

```csharp
public class ApiSecrets : IAppSettings
{
    public static string Key => "ApiSecrets";

    [AppSettingsEncryption]
    public string ApiKey { get; set; } = string.Empty; // decrypts back to "hunter2"
}
```

`-Value` isn't limited to a single string — pipe a JSON document in instead when the value belongs to
a **class-level** attribute:

```powershell
PS> '{"Foo":"baz","Bar":7}' | ./Encrypt-AppSetting.ps1 -Key $base64Key
```

**Mode B — `-Path`: encrypt a file's contents in full.** Instead of typing or piping the plaintext,
point the script at a file and it encrypts that file's entire contents (`Get-Content -Raw`) as one
piece of ciphertext — the same output you'd get from `-Value` with that same text, just without
having to paste a large JSON document on the command line. This is the practical way to produce the
ciphertext for a class-level attribute's JSON:

```json
// secrets.json — the plaintext shape of the whole settings object
{ "Foo": "baz", "Bar": 7 }
```

```powershell
PS> ./Encrypt-AppSetting.ps1 -Path .\secrets.json -Key $base64Key
Cipher text: 7Kj2mPQ8vXzL4nR9wT1eYcAqB3sHdMoV6uIfN0gJp2k=
Key: <the key you passed in>
```

That single cipher text becomes the **whole value** of the section's key in `appsettings.json` —
notice there's no nested object here, just one string:

```json
{ "WholeSectionSecret": "7Kj2mPQ8vXzL4nR9wT1eYcAqB3sHdMoV6uIfN0gJp2k=" }
```

```csharp
[AppSettingsEncryption] // on the class, not a property
public class WholeSectionSecret : IAppSettings
{
    public static string Key => "WholeSectionSecret";

    public string Foo { get; set; } = string.Empty; // decrypts+deserializes back to "baz"
    public int Bar { get; set; }                     // and 7
}
```

> ⚠️ **Known limitation:** `-Path` can encrypt *any* file, but the result is only usable if you feed
> it back in correctly. There's no supported way to point a whole configuration source at a bare
> ciphertext file — the ciphertext always has to end up as the string value of a key inside a JSON
> document (`{ "WholeSectionSecret": "<ciphertext>" }` above, not a standalone file containing nothing
> but the ciphertext), because `IConfiguration`'s JSON provider needs a JSON document to parse in the
> first place. And for a class-level attribute specifically, the *decrypted* plaintext also has to be
> JSON matching the model's shape — `AppSettingsEncryptionStep` runs it straight through
> `JsonSerializer.Deserialize<TConfigType>`, so encrypting some arbitrary non-JSON text file (a plain
> secrets `.txt`, for instance) decrypts fine but then throws a `JsonException` on deserialization.

**Other ways to run it:**

```powershell
# Omit -Key to have a random AES-128 key generated and printed alongside the cipher text —
# useful the first time, before you have a key to reuse
./Encrypt-AppSetting.ps1 -Value "hunter2"

# No parameters at all: it asks interactively whether to encrypt typed text or a file, then
# prompts for whichever one you pick
./Encrypt-AppSetting.ps1
```

Whatever key you used (typed, or the randomly generated one printed alongside the cipher text) is
what you register with `AddKey` in part 2 below — a base64-encoded (or plain UTF-8) 16/24/32-byte
string for AES-128/192/256.

### 2. Registering keys and marking properties

Register at least one decryption key once, near the top of `Program.cs`:

```csharp
using AwadyLab.ConfigurationUtilities.Encryption;

builder.AddAppSettingsEncryption(options => options
    .AddKey(Convert.FromBase64String(defaultKeyBase64))       // resolved immediately, no DI needed
    .AddKey(vaultKeyBytes, keyName: "Vault")                   // a second, named key
    .UseKeyProvider((keyName, services) =>                     // fallback: resolve a key via DI
        services.GetRequiredService<ISecretStore>().GetKey(keyName)));
```

Then mark the property (or, for a section that's a single encrypted JSON blob, the whole class —
matching the `-Path` form of the script above):

```csharp
public class ApiSecrets : IAppSettings
{
    public static string Key => "ApiSecrets";

    [AppSettingsEncryption]              // decrypts with the "Default" key
    public string ApiKey { get; set; } = string.Empty;

    [AppSettingsEncryption("Vault")]     // decrypts with the "Vault" key
    public string VaultToken { get; set; } = string.Empty;
}
```

> ℹ️ **Note:** a key added via `AddKey` decrypts **eagerly** — the value returned by `AddOptions<T>()`
> is already plaintext. A key only reachable through `UseKeyProvider` needs DI, so it can't run
> until the host is built; that property stays ciphertext on the eagerly-returned model and only
> decrypts once `IOptions<T>` is first resolved.

**`GetConnectionStringWithEncryptPassword`.** A connection string is a single string value, not a
settings object with individually-typed properties — `[AppSettingsEncryption]` has nothing to attach
to. This extension method covers that case: it decrypts just the password embedded inside an
otherwise-plaintext connection string, rather than requiring the whole string to be encrypted.

```json
{
  "ConnectionStrings": {
    "Default": "Server=.;User Id=sa;Password=3q2+7wABAgMEBQYHCAkKC2j8k7X9mPz1nOe4wA==;"
  }
}
```

```csharp
using AwadyLab.ConfigurationUtilities.Encryption;

var connectionString = app.Configuration.GetConnectionStringWithEncryptPassword("Default", app.Services);
// "Server=.;User Id=sa;Password=hunter2;"
```

This reads `ConnectionStrings:Default`, decrypts whatever's in its `Password`/`Pwd` keyword (both are
recognized, since either can appear in a real connection string), and returns the connection string
with the plaintext password substituted back in — everything else in the string is left untouched.
The ciphertext itself is produced the same way as any other encrypted value, via
`Encrypt-AppSetting.ps1 -Value` (see [Producing the ciphertext](#1-producing-the-ciphertext)), using
whichever key `AddAppSettingsEncryption` resolves as the `"Default"`-named key.

A missing connection string name returns `null` rather than throwing. Calling this method without
having called `AddAppSettingsEncryption` first throws — there's no decryptor to resolve.

> ⚠️ **Known limitation:** *property-level* encryption (`[AppSettingsEncryption]` on a property) only
> works when that property's type is `string`. Binding runs *before* decryption, so if you mark a
> non-string property (e.g. `int`), the plain `ConfigurationBinder` tries to parse the still-encrypted
> ciphertext into that type first and throws, before decryption ever gets a chance to run.
>
> This does **not** apply to *class-level* encryption (`[AppSettingsEncryption]` on the class, the
> `-Path` form from part 1 above) — there, the whole section is one encrypted JSON blob, so it's
> decrypted and deserialized directly, bypassing `ConfigurationBinder` altogether. Non-string
> properties (like `Bar` in the `WholeSectionSecret` example above) work normally in that mode.

### 3. Changing the encryption algorithm

Decryption is behind an interface, `IAppSettingsDecryptor`, defaulting to a built-in AES-GCM
implementation. Swap in your own implementation to use a different algorithm — or to call out to an
external decryption service instead of doing it in-process:

```csharp
public class MyDecryptor : IAppSettingsDecryptor
{
    public string Decrypt(string cipherText, byte[] key) => /* your algorithm */;
}
```

```csharp
builder.Services.AddSingleton<IAppSettingsDecryptor, MyDecryptor>();
builder.AddAppSettingsEncryption(options => options.UseKeyProvider(...));
```

> ⚠️ **Important caveat:** a custom `IAppSettingsDecryptor` only takes effect for keys resolved through
> `UseKeyProvider` — the eager `AddKey` decryption pass runs before the host is built, with no DI
> container available yet, so it always uses the built-in AES-GCM decryptor regardless of what's
> registered. If you need a different algorithm to apply everywhere, register your keys only via
> `UseKeyProvider` (returning them from a static source is fine — DI just needs to be available).

---

## Feature: Polymorphic (discriminated-union) binding

**Why.** Configuration sometimes needs to describe "one of several shapes" — a notification channel
that's either email or Slack, a storage backend that's either local disk or S3. Plain
`ConfigurationBinder` can't construct an interface or abstract class, so there's normally no clean
way to express this in JSON at all.

**When.** Any property whose concrete type should be picked based on a value elsewhere in the same
config section — a `"Type": "Slack"` discriminator field, most commonly.

Declare the options once, on the interface/base type — both parts below reuse the same declaration:

```csharp
using AwadyLab.ConfigurationUtilities.PolymorphicBinding;

[PolymorphicOption("Email", typeof(EmailChannel))]
[PolymorphicOption("Slack", typeof(SlackChannel))]
public interface INotificationChannel;

public class EmailChannel : INotificationChannel { public string Address { get; set; } = ""; }
public class SlackChannel : INotificationChannel { public string Webhook { get; set; } = ""; }
```

### 1. A single polymorphic object

**How.**

```csharp
public class NotificationSettings : IAppSettings
{
    public static string Key => "Notifications";

    [PolymorphicSection] // reads a "Type" key by default; pass a custom key name if you need one
    public INotificationChannel Channel { get; set; } = null!;
}
```

```json
{ "Notifications": { "Channel": { "Type": "Slack", "Webhook": "https://hooks.slack.example/..." } } }
```

### 2. A collection of polymorphic objects

**Why/When.** The same discriminated-union need, but for a *list* of items rather than one —
e.g. an app that can notify through several channels at once, each configured independently.

**How.** The same `[PolymorphicSection]` attribute also works on a list or array — each element gets
its own discriminator:

```csharp
public class NotificationSettings : IAppSettings
{
    public static string Key => "Notifications";

    [PolymorphicSection]
    public List<INotificationChannel> Channels { get; set; } = [];
}
```

```json
{
  "Notifications": {
    "Channels": [
      { "Type": "Email", "Address": "ops@example.com" },
      { "Type": "Slack", "Webhook": "https://hooks.slack.example/..." }
    ]
  }
}
```

`IList<T>` and `T[]` work the same way as `List<T>`. Each item is resolved and bound independently —
a mix of channel types in the same list is fine.

An unknown or missing discriminator (on a single object or on any list item) throws a clear
`InvalidOperationException` at startup rather than binding something wrong silently.

> ⚠️ **Known limitation:** the `[PolymorphicSection]` attribute is required. A `List<T>`/interface
> property left unmarked isn't rejected — the plain `ConfigurationBinder` just silently binds it to
> an **empty list**, since it can't construct the interface-typed elements and doesn't fail loudly
> the way it does for a single object property. Always mark polymorphic collections explicitly.

---

## Feature: Integrity signature verification

**Why.** For configuration that must be tamper-evident — a license, an entitlement, anything you
want cryptographic proof wasn't altered after it left wherever it was issued.

**When.** High-trust configuration where you control both the signing side (at release/config-issue
time) and the verifying side (at app startup), and want startup to fail loudly on any mismatch.

**How.**

```csharp
using AwadyLab.ConfigurationUtilities.Integrity;

builder.AddOptions<LicenseSettings>(pipeline => pipeline.UseIntegritySignature(new IntegritySignatureOptions
{
    SignatureProvider = () => File.ReadAllBytes("license.sig"),
    CertificateProvider = () => new X509Certificate2("license.cer"), // public cert
}));
```

The default verifier (`X509CertificateSignatureVerifier`) checks a SHA-256 signature against the
certificate's RSA or ECDSA public key, over the JSON-serialized bound model. It runs as a hosted
service (aggregated the same way FluentValidation's checks are — one shared hosted service, not one
per type), so it verifies once the host starts and fails startup if the signature doesn't match.

This step is opt-in only — it's not part of the default pipeline, since most config doesn't need
signature verification.

### Changing the verification algorithm/provider

`CertificateProvider` only matters for the *default* verifier. To use a different algorithm
entirely (HMAC, a different hash, a call out to a signing service) or resolve trust material from
DI instead of a file, replace `Verifier` and skip `CertificateProvider` altogether:

```csharp
using System.Security.Cryptography;

public class HmacSignatureVerifier(byte[] sharedSecret) : ISettingsSignatureVerifier
{
    public bool Verify(byte[] data, byte[] signature) =>
        CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(sharedSecret, data), signature);
}
```

```csharp
pipeline.UseIntegritySignature(new IntegritySignatureOptions
{
    SignatureProvider = () => signatureBytes,
    Verifier = services => new HmacSignatureVerifier(
        services.GetRequiredService<ISecretStore>().GetSharedSecret()),
});
```

`Verifier` is handed the fully-built `IServiceProvider`, so it can resolve a certificate, a secret,
or anything else it needs from DI — the same escape hatch the encryption feature offers via
`IAppSettingsDecryptor`.

---

## Feature: Custom configuration sources

**Why.** Sometimes config doesn't live in a file at all — it comes from a remote config service, a
database row, a blob store. You still want it to flow through the exact same binding, validation,
and `IOptionsMonitor<T>` change-notification pipeline as `appsettings.json`.

**When.** Any time the source of truth for a section is something other than a local file or
environment variables.

**How.** Implement `IAppSettingsProvider` (one method: return the current settings as JSON) and
register it:

```csharp
using AwadyLab.ConfigurationUtilities.Providers;

public class RemoteFeatureFlagsProvider : IAppSettingsProvider
{
    public string GetSettings() => httpClient.GetStringAsync("https://config.example/flags").Result;
}
```

```csharp
builder.AddAppSettingsProvider(new RemoteFeatureFlagsProvider(), options =>
{
    options.ReloadInterval = TimeSpan.FromMinutes(1); // re-poll on this interval
    options.ThrowIfFailFirstTime = true;               // fail startup if the very first load fails
    options.KeepOldValueOnReloadFailure = true;         // a later failed poll keeps the last-known-good values
});

builder.AddOptions<FeatureFlagsSettings>();
```

A change in the provider's returned JSON triggers the normal configuration-reload token, so
`IOptionsMonitor<T>` picks it up automatically — no different from editing `appsettings.json` on
disk with file-watching enabled.

### Beyond feature flags: key rotation and rotating integrity signatures

`IAppSettingsProvider` isn't limited to feeding `AddOptions<T>` sections — it's a general "this
value can change at runtime, and I want the normal reload machinery to notice" primitive. Two other
features in this package are built around exactly that same idea (a callback that's re-invoked
rather than a fixed value), so all three combine naturally.

#### Key rotation

**Why.** A rotated encryption key is only safe if the secret it protects gets re-encrypted under the
new key at the same time. Doing that by hand (redeploy the app with a new key *and* new ciphertext,
in the right order, with no window where they're mismatched) is exactly the kind of thing that goes
wrong under time pressure. Wiring both sides to the same live source removes the manual
coordination.

**How.** Two independent moving parts: something that hands back the *current* key
(`UseKeyProvider`), and something that hands back the *current* ciphertext, re-encrypted under
whatever key is current (`IAppSettingsProvider`). Neither has to know about the other — they just
both have to agree on what "current" means, e.g. by both asking the same secret store.

```csharp
using AwadyLab.ConfigurationUtilities.Encryption;
using AwadyLab.ConfigurationUtilities.Providers;

// Asks a secret store (Vault, AWS Secrets Manager, ...) for whatever is current right now.
public class VaultBackedKeyProvider(ISecretStoreClient secretStore)
{
    public byte[] GetCurrentKey(string keyName) => secretStore.GetCurrentKey(keyName);
}

// Fetches the secret, re-encrypted under whatever key is current, from the same secret store.
public class VaultBackedSecretsProvider(ISecretStoreClient secretStore) : IAppSettingsProvider
{
    public string GetSettings() =>
        $$"""{ "ApiSecrets": { "ApiKey": "{{secretStore.GetCurrentEncryptedApiKey()}}" } }""";
}
```

```csharp
builder.AddAppSettingsProvider(
    new VaultBackedSecretsProvider(secretStore),
    options => options.ReloadInterval = TimeSpan.FromMinutes(5));

builder.AddAppSettingsEncryption(options => options
    .UseKeyProvider((keyName, services) => new VaultBackedKeyProvider(secretStore).GetCurrentKey(keyName)));

builder.AddOptions<ApiSecrets>();
```

When the secret store rotates the key, it republishes the ciphertext at the same time — both change
together, from the app's point of view, on the next `ReloadInterval` poll (or the next
`IOptionsMonitor<T>` resolution after a manual `IConfigurationRoot.Reload()`, if you need it sooner
than that). No redeploy, no restart.

**What happens if the rotation is only half-done** — the key changes but the ciphertext isn't
republished yet, or vice versa — matters just as much as the happy path: decryption fails loudly
(a `CryptographicException` surfaces from `IOptionsMonitor<T>`) instead of silently producing
garbage. That's a feature, not an edge case to work around: it turns "the app is quietly using the
wrong secret" into "the app told you immediately that something's inconsistent."

#### Rotating integrity signatures and certificates

**Why.** A license or entitlement's signing certificate eventually expires or gets reissued; a
signature covering config that changed needs re-signing. `IntegritySignatureOptions.SignatureProvider`
and `CertificateProvider` are callbacks precisely so this doesn't require a code change or restart
when it happens.

**How.** Point both at the same live source your signing/issuing process publishes to:

```csharp
using AwadyLab.ConfigurationUtilities.Integrity;

public class LicenseAuthorityClient
{
    public byte[] GetCurrentSignature() => /* fetch the latest signature */;
    public X509Certificate2 GetCurrentCertificate() => /* fetch the current signing certificate */;
}
```

```csharp
var licenseAuthority = new LicenseAuthorityClient();

builder.AddOptions<LicenseSettings>(pipeline => pipeline.UseIntegritySignature(new IntegritySignatureOptions
{
    SignatureProvider = () => licenseAuthority.GetCurrentSignature(),
    CertificateProvider = () => licenseAuthority.GetCurrentCertificate(),
}));
```

Both callbacks are invoked fresh on every verification pass, not cached at startup — a certificate
renewal or a newly issued signature on the authority's side takes effect the next time the check
runs, with nothing to redeploy. If the source of the signature/certificate needs its own DI
dependencies (an `HttpClient`, a secrets client), skip these two callbacks and resolve everything
inside a custom `Verifier` instead (see
[Changing the verification algorithm/provider](#changing-the-verification-algorithmprovider)) —
`Verifier` is handed the fully-built `IServiceProvider` for exactly that purpose.

---

## Custom pipeline steps

**Why.** For the rare case where none of the above covers what you need, but you still want your
logic to run as part of the same ordered pipeline (with access to the `WebApplicationBuilder`, the
bound model, and the raw `IConfigurationSection`).

**How.**

```csharp
using AwadyLab.ConfigurationUtilities.Core;

public class LogSettingsStep : IOptionsSetupStep<MySettings>
{
    public void Setup(WebApplicationBuilder builder, MySettings model, IConfigurationSection section) =>
        Console.WriteLine($"Loaded {MySettings.Key}: {model}");
}
```

```csharp
builder.AddOptions<MySettings>(pipeline => pipeline.Use(new LogSettingsStep()));

// or, from a shared AddOptionsInAssembly callback (IOptionsPipelineBuilder), the generic form:
builder.AddOptionsInAssembly(assembly, configure: pipeline => pipeline.Use(new LogSettingsStep()));
```

A step registered this way is appended after the default pipeline (so it sees the fully bound,
decrypted, validated model). Calling `Use` again with a step of the *same type* replaces it in
place rather than moving it to the end — reconfiguring a step never silently reorders it relative to
everything else.

---

## Error handling

`AddOptions<T>()`/`AddOptionsInAssembly()` throw at startup, not later, for:

- The configuration section not existing at all
- An unknown or missing polymorphic discriminator
- A failing custom validator (`IAppSettingValidator`)
- A `[ForbiddenInProductionWhen]` value present in Production
- Decryption failure (wrong key, tampered ciphertext)
- Failing DataAnnotations/FluentValidation checks (surfacing on first `IOptions<T>` access, or at
  host startup if `ValidateOnStart` is on, which is the default for DataAnnotations)
- A failed integrity signature check

Example messages:

```
Configuration section 'Database' not found in appsettings.json
No PolymorphicOption registered for Type = 'Fax' on 'INotificationChannel' (property 'NotificationSettings.Channel').
Options validation failed: JWT secret must be at least 32 characters
'DockerMonitorSettings.AllowInsecureSocket' is set to 'True', which is forbidden in Production.
```

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