using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.Encryption;

namespace SampleApp.Models;

/// <summary>
/// Demonstrates <see cref="AppSettingsEncryptionAttribute"/> on individual properties. <see cref="ApiKey"/>
/// decrypts with the "Default" key (registered via <c>AddKey</c>, resolved eagerly). <see cref="VaultToken"/>
/// decrypts with the "Vault" key, which is only reachable through <c>UseKeyProvider</c> — so it stays
/// encrypted on the eagerly-returned model and only gets decrypted once <c>IOptions&lt;ApiSecrets&gt;</c>
/// is first resolved after the host is built.
/// </summary>
public class ApiSecrets : IAppSettings
{
    public static string Key => "ApiSecrets";

    [AppSettingsEncryption]
    public string ApiKey { get; set; } = string.Empty;

    [AppSettingsEncryption("Vault")]
    public string VaultToken { get; set; } = string.Empty;
}
