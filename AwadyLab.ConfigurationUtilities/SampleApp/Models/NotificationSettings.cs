using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.Binding;
using AwadyLab.ConfigurationUtilities.PolymorphicBinding;

namespace SampleApp.Models;

public class NotificationSettings : IAppSettings
{
    public static string Key => "Notifications";

    [PolymorphicSection]
    public INotificationChannel Channel { get; set; } = null!;
}
