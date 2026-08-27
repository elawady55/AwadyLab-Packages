using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AwadyLab.ConfigurationUtilities.Core;
using AwadyLab.ConfigurationUtilities.Encryption;
using AwadyLab.ConfigurationUtilities.Integrity;
using AwadyLab.ConfigurationUtilities.Providers;
using FluentValidation;
using Microsoft.Extensions.Options;
using SampleApp.Models;
using SampleApp.Providers;
using VaultAgent.Models;

var builder = WebApplication.CreateBuilder(args);
var assembly = Assembly.GetExecutingAssembly();

// --- Encryption: register the keys AppSettingsEncryptionAttribute decrypts with (see ApiSecrets). ---
// "Default" is resolved eagerly via AddKey; "Vault" is only reachable through UseKeyProvider, so it
// simulates pulling a key from a secret store that needs DI (only available once the host is built).
var defaultKey = Convert.FromBase64String("INdX04z1/0D+68Vr0vj/CC2RAIWBTo13R2Z32z+uZ0M=");
var vaultKey = Convert.FromBase64String("ogkVpvOIO5siysK015ioDLRV07W+nHkcV0Ex7rqaS2k=");
builder.AddAppSettingsEncryption(options => options
    .AddKey(defaultKey)
    .UseKeyProvider((keyName, _) => keyName == "Vault"
        ? vaultKey
        : throw new InvalidOperationException($"No key provider result for '{keyName}'.")));

// --- Custom configuration source: FeatureFlags comes from StaticFeatureFlagsProvider, not appsettings.json. ---
builder.AddAppSettingsProvider(new StaticFeatureFlagsProvider(),
    options => options.ReloadInterval = TimeSpan.FromSeconds(15));

// --- Bulk registration: every IAppSettings type in this assembly gets the default pipeline (bind,
// encryption, environment overrides, forbidden-in-production, data annotations, custom validation,
// fluent validation), plus this shared tweak applied to all of them. SignedLicenseSettings is excluded
// because it needs a bespoke UseIntegritySignature configuration that doesn't apply to any other type. ---
builder.AddOptionsInAssembly(assembly,
    configure: pipeline => pipeline.UseDataAnnotations(o => o.ValidateOnStart = true),
    exclude: [typeof(SignedLicenseSettings)]);

// --- FluentValidation: DatabaseSettingsValidator is picked up by UseFluentValidation via reflection. ---
builder.Services.AddValidatorsFromAssembly(assembly);

// --- Integrity signature: registered individually since the certificate/signature are specific to this
// one type. The demo signs the section with an ephemeral self-signed certificate generated at startup;
// a real app would supply a production certificate and a signature produced out-of-band at release time. ---
var licenseCertificate = CreateSelfSignedCertificate();
var licensePayload = JsonSerializer.SerializeToUtf8Bytes(
    builder.Configuration.GetSection(SignedLicenseSettings.Key).Get<SignedLicenseSettings>());
var licenseSignature = licenseCertificate.GetRSAPrivateKey()!
    .SignData(licensePayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

builder.AddOptions<SignedLicenseSettings>(pipeline => pipeline.UseIntegritySignature(new IntegritySignatureOptions
{
    SignatureProvider = () => licenseSignature,
    CertificateProvider = () => licenseCertificate,
}));

var app = builder.Build();

app.MapGet("/vault", (IOptions<VaultConfiguration> o) => o.Value);
app.MapGet("/docker", (IOptions<DockerMonitorSettings> o) => o.Value);
app.MapGet("/certificate", (IOptions<CertificateSettings> o) => o.Value);
app.MapGet("/api-secrets", (IOptions<ApiSecrets> o) => o.Value);
app.MapGet("/notifications", (IOptions<NotificationSettings> o) => Results.Ok(new
{
    ChannelType = o.Value.Channel.GetType().Name,
    Channel = (object)o.Value.Channel,
}));
app.MapGet("/database", (IOptions<DatabaseSettings> o) => o.Value);
app.MapGet("/license", (IOptions<SignedLicenseSettings> o) => o.Value);
app.MapGet("/feature-flags", (IOptionsMonitor<FeatureFlagsSettings> o) => o.CurrentValue);

app.MapGet("/connection-string", (IConfiguration configuration, IServiceProvider services) =>
    Results.Ok(new { ConnectionString = configuration.GetConnectionStringWithEncryptPassword("Default", services, "Vault") }));

app.Run();
return;

static X509Certificate2 CreateSelfSignedCertificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=SampleApp Demo License", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
}
