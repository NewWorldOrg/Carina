using System.Globalization;

using Carina.Domain.Channels;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class LogoSweepOptions
{
    public const string Section = "Logos";

    public string? BetweenSweeps { get; set; }

    public string? LongestVisit { get; set; }

    public string? BetweenVisits { get; set; }

    public string? BeforeRetrying { get; set; }

    public string? Collects { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BetweenSweeps = named[nameof(BetweenSweeps)];
        LongestVisit = named[nameof(LongestVisit)];
        BetweenVisits = named[nameof(BetweenVisits)];
        BeforeRetrying = named[nameof(BeforeRetrying)];
        Collects = named[nameof(Collects)];
    }

    public LogoSweepSettings Read()
    {
        LogoSweepSettings unset = new();
        LogoSweepSettings read = new()
        {
            BetweenSweeps = Positive(BetweenSweeps, nameof(BetweenSweeps), unset.BetweenSweeps),
            LongestVisit = Positive(LongestVisit, nameof(LongestVisit), unset.LongestVisit),
            BetweenVisits = Positive(BetweenVisits, nameof(BetweenVisits), unset.BetweenVisits),
            BeforeRetrying = Positive(BeforeRetrying, nameof(BeforeRetrying), unset.BeforeRetrying),
            Collects = Either(Collects, nameof(Collects), unset.Collects),
        };

        return Agreeing(read);
    }

    private static LogoSweepSettings Agreeing(LogoSweepSettings read)
    {
        if (read.BeforeRetrying > read.BetweenVisits)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(BeforeRetrying)} is longer than {Section}:{nameof(BetweenVisits)}, "
                + "so a transport that gave nothing would be asked again later than one that gave a logo.",
                nameof(BeforeRetrying));
        }

        if (read.LongestVisit >= read.BetweenSweeps)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(LongestVisit)} reaches the whole of {Section}:{nameof(BetweenSweeps)}, "
                + "which leaves a tuner held for as long as the sweep runs rather than between sweeps.",
                nameof(LongestVisit));
        }

        return read;
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

    private static bool Either(string? setting, string name, bool unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return bool.TryParse(setting, out bool read)
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} reads either true or false, and '{setting}' is neither.",
                name);
    }
}

public sealed class LogoSweepValidation : IValidateOptions<LogoSweepOptions>
{
    public ValidateOptionsResult Validate(string? name, LogoSweepOptions options)
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
