using System.Globalization;

using Carina.Domain.Quality;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class QualitySignalOptions
{
    public const string Section = "QualitySignal";

    public const string Forever = "forever";

    public string? BeforeFirstSample { get; set; }

    public string? BetweenSamples { get; set; }

    public string? BeforeFirstRollup { get; set; }

    public string? BetweenRollups { get; set; }

    public string? KeepSamplesFor { get; set; }

    public string? KeepMinuteWindowsFor { get; set; }

    public string? KeepHourWindowsFor { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BeforeFirstSample = named[nameof(BeforeFirstSample)];
        BetweenSamples = named[nameof(BetweenSamples)];
        BeforeFirstRollup = named[nameof(BeforeFirstRollup)];
        BetweenRollups = named[nameof(BetweenRollups)];
        KeepSamplesFor = named[nameof(KeepSamplesFor)];
        KeepMinuteWindowsFor = named[nameof(KeepMinuteWindowsFor)];
        KeepHourWindowsFor = named[nameof(KeepHourWindowsFor)];
    }

    public QualitySignalSettings Read()
    {
        QualitySignalSettings unset = new();

        QualitySignalSettings read = new()
        {
            BeforeFirstSample = Positive(BeforeFirstSample, nameof(BeforeFirstSample), unset.BeforeFirstSample),
            BetweenSamples = Positive(BetweenSamples, nameof(BetweenSamples), unset.BetweenSamples),
            BeforeFirstRollup = Positive(BeforeFirstRollup, nameof(BeforeFirstRollup), unset.BeforeFirstRollup),
            BetweenRollups = Positive(BetweenRollups, nameof(BetweenRollups), unset.BetweenRollups),
            KeepSamplesFor = Positive(KeepSamplesFor, nameof(KeepSamplesFor), unset.KeepSamplesFor),
            KeepWindowsFor = new Dictionary<QualityWindow, TimeSpan?>
            {
                [QualityWindow.Minute] = Kept(
                    KeepMinuteWindowsFor,
                    nameof(KeepMinuteWindowsFor),
                    unset.KeptFor(QualityWindow.Minute)),
                [QualityWindow.Hour] = Kept(
                    KeepHourWindowsFor,
                    nameof(KeepHourWindowsFor),
                    unset.KeptFor(QualityWindow.Hour)),
            },
        };

        return Agreeing(read);
    }

    private static QualitySignalSettings Agreeing(QualitySignalSettings read)
    {
        if (read.KeptFor(QualityWindow.Minute) is { } minutes && read.KeepSamplesFor > minutes)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(KeepMinuteWindowsFor)} is shorter than {Section}:{nameof(KeepSamplesFor)}, "
                + "so the windows a sample was rolled into would go before the sample itself.",
                nameof(KeepMinuteWindowsFor));
        }

        if (read.KeptFor(QualityWindow.Hour) is { } hours
            && read.KeptFor(QualityWindow.Minute) is { } shorter
            && shorter > hours)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(KeepHourWindowsFor)} is shorter than {Section}:{nameof(KeepMinuteWindowsFor)}, "
                + "so the layer kept for the long run would go before the one kept for the short one.",
                nameof(KeepHourWindowsFor));
        }

        return read;
    }

    private static TimeSpan? Kept(string? setting, string name, TimeSpan? unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return string.Equals(setting.Trim(), Forever, StringComparison.OrdinalIgnoreCase)
            ? null
            : Positive(setting, name, TimeSpan.Zero);
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
}

public sealed class QualitySignalValidation : IValidateOptions<QualitySignalOptions>
{
    public ValidateOptionsResult Validate(string? name, QualitySignalOptions options)
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
