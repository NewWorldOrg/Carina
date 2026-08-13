using System.ComponentModel.DataAnnotations;

namespace Carina.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string ConnectionStringName = "Carina";

    [Required(ErrorMessage = "ConnectionStrings:Carina (environment variable ConnectionStrings__Carina) must be set.")]
    public string? ConnectionString { get; set; }
}
