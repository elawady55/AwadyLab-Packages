using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.PolymorphicBinding;

namespace SampleApp.Models;

public class NotificationSettings : IAppSettings
{
    public static string Key => "Notifications";

    [PolymorphicSection]
    public NotificationChannel Channel { get; set; } = null!;
}
