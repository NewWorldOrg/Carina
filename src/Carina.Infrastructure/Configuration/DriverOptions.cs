using System.ComponentModel.DataAnnotations;

namespace Carina.Infrastructure.Configuration;

public sealed class DriverOptions
{
    public const string SocketPathKey = "CARINA_DRIVER_SOCKET";

    public const string DefaultSocketPath = "/run/carina/driver.sock";

    [Required(ErrorMessage = "CARINA_DRIVER_SOCKET must name the driver socket path when it is set.")]
    public string? SocketPath { get; set; }
}
