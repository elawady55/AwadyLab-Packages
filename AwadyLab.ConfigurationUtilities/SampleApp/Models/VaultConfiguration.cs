using System.ComponentModel.DataAnnotations;
using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.Core;
using AwadyLab.ConfigurationUtilities.EnvironmentVariables;
using FluentValidation;

namespace VaultAgent.Models;

public class VaultConfiguration : IAppSettings
{
    public static string Key => "Vault";


    public string VaultAddress { get; set; } = "http://localhost:8200";
    public string RoleId { get; set; } = string.Empty;
    [EnvironmentVariable("DDD")]
    public string SecretId { get; set; } = string.Empty;
    public string SecretPath { get; set; } = "secret/data/certificates";
    
    public string PkiPath { get; set; } = "pki";
    public int TokenRenewalThresholdSeconds { get; set; } = 300; // 5 minutes before expiry
    public int CertificateRenewalThresholdSeconds { get; set; } = 86400; // 1 day before expiry
}

