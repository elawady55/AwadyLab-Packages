using System.ComponentModel.DataAnnotations;
using AwadyLab.ConfigurationUtilities;
using AwadyLab.ConfigurationUtilities.ForbiddenInProduction;

namespace SampleApp.Models;

/// <summary>Demonstrates DataAnnotations validation and <see cref="ForbiddenInProductionWhenAttribute"/>.</summary>
public class DockerMonitorSettings : IAppSettings
{
    public static string Key => "Docker";

    [Required]
    public string SocketPath { get; set; } = "unix:///var/run/docker.sock";

    [Required]
    public string LabelPrefix { get; set; } = "traefik.http.routers";

    [Range(5, 3600)]
    public int MonitoringIntervalSeconds { get; set; } = 30;

    /// <summary>Only meant for local debugging — startup fails if this is still true in Production.</summary>
    [ForbiddenInProductionWhen(true)]
    public bool AllowInsecureSocket { get; set; }
}
