using System.Globalization;

using Carina.Domain.Streaming;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class TranscodingOptions
{
    public const string Section = "Transcoding";

    public string? AtOnce { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        AtOnce = named[nameof(AtOnce)];
    }

    public TranscodeBudgetSettings Read()
    {
        TranscodeBudgetSettings unset = new();

        return new TranscodeBudgetSettings
        {
            AtOnce = Counted(AtOnce, nameof(AtOnce), unset.AtOnce, TranscodeBudgetSettings.Fewest),
        };
    }

    private static int Counted(string? setting, string name, int unset, int lowest)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        int read = int.TryParse(setting, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"{Section}:{name} reads a whole number, and '{setting}' is not one.", name);

        return read >= lowest
            ? read
            : throw new ArgumentException($"{Section}:{name} is at least {lowest}, and '{setting}' is not.", name);
    }
}

public sealed class TranscodingValidation : IValidateOptions<TranscodingOptions>
{
    public ValidateOptionsResult Validate(string? name, TranscodingOptions options)
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
