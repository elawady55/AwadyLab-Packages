using AwadyLab.ConfigurationUtilities;

namespace SampleApp.Models;

/// <summary>Sourced from <see cref="SampleApp.Providers.StaticFeatureFlagsProvider"/>, not appsettings.json.</summary>
public class FeatureFlagsSettings : IAppSettings
{
    public static string Key => "FeatureFlags";

    public bool NewCheckoutFlow { get; set; }
    public bool BetaDashboard { get; set; }
}
