using AwadyLab.ConfigurationUtilities;

namespace SampleApp.Models;

/// <summary>
/// Demonstrates <c>UseIntegritySignature</c> — registered individually in <c>Program.cs</c> (and excluded
/// from the <c>AddOptionsInAssembly</c> bulk scan) because its signature/certificate are specific to this
/// one type, not something every config type in the assembly shares.
/// </summary>
public class SignedLicenseSettings : IAppSettings
{
    public static string Key => "License";

    public string LicensedTo { get; set; } = string.Empty;
    public int MaxSeats { get; set; }
    public string ExpiresOn { get; set; } = string.Empty;
}
