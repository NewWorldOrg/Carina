using System.ComponentModel.DataAnnotations;

namespace Carina.Infrastructure.Configuration;

public sealed class SealingKeyOptions
{
    public const string DirectoryKey = "CARINA_DATA_PROTECTION_KEYS";

    public const string ApplicationName = "Carina";

    [Required(ErrorMessage = "CARINA_DATA_PROTECTION_KEYS must be set to a directory that outlives the container.")]
    public string? Directory { get; set; }
}
