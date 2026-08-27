using AwadyLab.ConfigurationUtilities;

namespace SampleApp.Models;

/// <summary>Demonstrates FluentValidation via <c>UseFluentValidation</c> + <c>AddValidatorsFromAssembly</c>.</summary>
public class DatabaseSettings : IAppSettings
{
    public static string Key => "Database";

    public string ConnectionStringName { get; set; } = "Default";
    public int MaxPoolSize { get; set; } = 100;
    public int CommandTimeoutSeconds { get; set; } = 30;
}
