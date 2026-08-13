using System.ComponentModel.DataAnnotations;

namespace Carina.Infrastructure.Configuration;

public sealed class DriverOptions
{
    public const string SocketPathKey = "CARINA_DRIVER_SOCKET";

    [Required(ErrorMessage = "CARINA_DRIVER_SOCKET must be set to the driver socket path.")]
    public string? SocketPath { get; set; }
}
