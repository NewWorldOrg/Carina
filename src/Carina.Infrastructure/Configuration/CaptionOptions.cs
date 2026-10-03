using System.Globalization;

using Carina.Domain.Captions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class CaptionOptions
{
    public const string Section = "Captions";

    public string? WrittenTo { get; set; }

    public string? BeforeFirstPass { get; set; }

    public string? BetweenPasses { get; set; }

    public string? LongestTranscription { get; set; }

    public string? AtMostAPass { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        WrittenTo = named[nameof(WrittenTo)];
        BeforeFirstPass = named[nameof(BeforeFirstPass)];
        BetweenPasses = named[nameof(BetweenPasses)];
        LongestTranscription = named[nameof(LongestTranscription)];
        AtMostAPass = named[nameof(AtMostAPass)];
    }

    public CaptionSettings Read()
    {
        CaptionSettings unset = new();

        return new CaptionSettings
        {
            WrittenTo = Absolute(WrittenTo, nameof(WrittenTo)),
            BeforeFirstPass = Positive(BeforeFirstPass, nameof(BeforeFirstPass), unset.BeforeFirstPass),
            BetweenPasses = Positive(BetweenPasses, nameof(BetweenPasses), unset.BetweenPasses),
            LongestTranscription = Positive(LongestTranscription, nameof(LongestTranscription), unset.LongestTranscription),
            AtMostAPass = Counted(AtMostAPass, nameof(AtMostAPass), unset.AtMostAPass),
        };
    }

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

    private static TimeSpan Positive(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        TimeSpan read = TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a duration as [d.]hh:mm:ss, and '{setting}' is not one.",
                name);

        return read > TimeSpan.Zero
            ? read
            : throw new ArgumentException($"{Section}:{name} has to be longer than nothing.", name);
    }

    private static int Counted(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        int read = int.TryParse(setting, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"{Section}:{name} reads a whole number, and '{setting}' is not one.", name);

        return read >= 1
            ? read
            : throw new ArgumentException($"{Section}:{name} is at least 1, and '{setting}' is not.", name);
    }
}

public sealed class CaptionValidation : IValidateOptions<CaptionOptions>
{
    public ValidateOptionsResult Validate(string? name, CaptionOptions options)
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
