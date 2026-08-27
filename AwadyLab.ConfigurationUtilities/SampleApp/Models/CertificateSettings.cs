using System.ComponentModel.DataAnnotations;
using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.Validators.CustomValidator;

namespace SampleApp.Models;

/// <summary>Demonstrates the custom-validation hook via <see cref="IAppSettingValidator"/>.</summary>
public class CertificateSettings : IAppSettingValidator
{
    public static string Key => "Certificate";

    public string OutputDirectory { get; set; } = "/certs";

    /// <summary>A Go-style duration, e.g. "720h" — not something DataAnnotations can express, so it's
    /// checked here instead.</summary>
    public string TTL { get; set; } = "720h";

    public ValidationResult Validate() =>
        TTL.EndsWith('h') && int.TryParse(TTL[..^1], out var hours) && hours > 0
            ? ValidationResult.Success!
            : new ValidationResult($"'{nameof(TTL)}' must look like '720h' (a positive number of hours). Got '{TTL}'.");
}
