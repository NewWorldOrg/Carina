using System.Globalization;

using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class RecordingProgressOptions
{
    public const string Section = "RecordingProgress";

    public string? AtMostEvery { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AtMostEvery = configuration.GetSection(Section)[nameof(AtMostEvery)];
    }

    public RecordingProgressSettings Read()
    {
        if (string.IsNullOrWhiteSpace(AtMostEvery))
        {
            return RecordingProgressSettings.Default;
        }

        TimeSpan read = TimeSpan.TryParse(AtMostEvery, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{nameof(AtMostEvery)} reads a duration as [d.]hh:mm:ss, and '{AtMostEvery}' is not one.",
                nameof(AtMostEvery));

        try
        {
            return new RecordingProgressSettings(read);
        }
        catch (ArgumentOutOfRangeException refusal)
        {
            throw new ArgumentException(
                $"{Section} does not describe a pace the screens can be told counts at: {refusal.Message}",
                refusal.ParamName,
                refusal);
        }
    }
}

public sealed class RecordingProgressValidation : IValidateOptions<RecordingProgressOptions>
{
    public ValidateOptionsResult Validate(string? name, RecordingProgressOptions options)
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
