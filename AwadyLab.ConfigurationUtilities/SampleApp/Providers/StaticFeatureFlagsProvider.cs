using AwadyLab.ConfigurationUtilities.Providers;

namespace SampleApp.Providers;

/// <summary>
/// Demonstrates <see cref="IAppSettingsProvider"/> / <c>AddAppSettingsProvider</c>: a stand-in for a
/// remote config service, database row, or blob-storage file — anything whose JSON should flow through the
/// same binding/validation pipeline as appsettings.json. Real implementations would fetch this from
/// wherever the flags actually live; this one just flips a value each call so the reload demo has
/// something to show.
/// </summary>
public sealed class StaticFeatureFlagsProvider : IAppSettingsProvider
{
    private bool _toggle;

    public string GetSettings()
    {
        _toggle = !_toggle;
        return $$"""
            { "FeatureFlags": { "NewCheckoutFlow": {{_toggle.ToString().ToLowerInvariant()}}, "BetaDashboard": true } }
            """;
    }
}
