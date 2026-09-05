using Carina.Domain.Encodings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class EncodingOptions
{
    public const string Section = "Encodings";

    public string? WorkedIn { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        WorkedIn = configuration.GetSection(Section)[nameof(WorkedIn)];
    }

    public EncodeSettings Read()
        => new()
        {
            WorkedIn = Absolute(WorkedIn, nameof(WorkedIn)),
        };

    private static string? Absolute(string? setting, string name)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return null;
        }

        return setting.StartsWith('/')
            ? setting
            : throw new ArgumentException(
                $"{Section}:{name} is written where the process can reach it, and '{setting}' is not absolute.",
                name);
    }
}

public sealed class EncodingValidation : IValidateOptions<EncodingOptions>
{
    public ValidateOptionsResult Validate(string? name, EncodingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.Read();
        }
        catch (ArgumentException refusal)
        {
            return ValidateOptionsResult.Fail(refusal.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
