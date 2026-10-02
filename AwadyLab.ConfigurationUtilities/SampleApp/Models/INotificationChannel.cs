using AwadyLab.ConfigurationUtilities.PolymorphicBinding;

namespace SampleApp.Models;

/// <summary>
/// Demonstrates <see cref="PolymorphicSectionAttribute"/>/<see cref="PolymorphicOptionAttribute"/>: the
/// concrete type bound for <see cref="NotificationSettings.Channel"/> is chosen by the "Type" discriminator
/// in configuration.
/// </summary>
[PolymorphicOption("Email", typeof(EmailNotificationChannel))]
[PolymorphicOption("Slack", typeof(SlackNotificationChannel))]
public interface INotificationChannel;

public class EmailNotificationChannel : INotificationChannel
{
    public string SmtpHost { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
}

public class SlackNotificationChannel : INotificationChannel
{
    public string WebhookUrl { get; set; } = string.Empty;
    public string ChannelName { get; set; } = "#alerts";
}
