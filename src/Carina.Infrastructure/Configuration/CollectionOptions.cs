using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class RotationBackoffOptions
{
    public string? FirstDelay { get; set; }

    public string? Factor { get; set; }

    public string? MaximumDelay { get; set; }

    public string? FailureCeiling { get; set; }
}

public sealed class CollectionOptions
{
    public const string Section = "Collection";

    public string? BetweenSweeps { get; set; }

    public string? WantedCoverage { get; set; }

    public string? RevisitsBelow { get; set; }

    public string? BetweenVisits { get; set; }

    public string? BeforeRetrying { get; set; }

    public string? LongestVisit { get; set; }

    public string? KeepEndedProgrammes { get; set; }

    public string? ArchiveRetention { get; set; }

    public string? LongestBackOff { get; set; }

    public string? BetweenBoosts { get; set; }

    public string? LongestBoost { get; set; }

    public string? RidesAlong { get; set; }

    public string? BetweenRideAlongSaves { get; set; }

    public string? BetweenSessionChecks { get; set; }

    public RotationBackoffOptions WhenTunersAreFull { get; } = new();

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BetweenSweeps = named[nameof(BetweenSweeps)];
        WantedCoverage = named[nameof(WantedCoverage)];
        RevisitsBelow = named[nameof(RevisitsBelow)];
        BetweenVisits = named[nameof(BetweenVisits)];
        BeforeRetrying = named[nameof(BeforeRetrying)];
        LongestVisit = named[nameof(LongestVisit)];
        KeepEndedProgrammes = named[nameof(KeepEndedProgrammes)];
        ArchiveRetention = named[nameof(ArchiveRetention)];
        LongestBackOff = named[nameof(LongestBackOff)];
        BetweenBoosts = named[nameof(BetweenBoosts)];
        LongestBoost = named[nameof(LongestBoost)];
        RidesAlong = named[nameof(RidesAlong)];
        BetweenRideAlongSaves = named[nameof(BetweenRideAlongSaves)];
        BetweenSessionChecks = named[nameof(BetweenSessionChecks)];

        IConfigurationSection full = named.GetSection(nameof(WhenTunersAreFull));

        WhenTunersAreFull.FirstDelay = full[nameof(RotationBackoffOptions.FirstDelay)];
        WhenTunersAreFull.Factor = full[nameof(RotationBackoffOptions.Factor)];
        WhenTunersAreFull.MaximumDelay = full[nameof(RotationBackoffOptions.MaximumDelay)];
        WhenTunersAreFull.FailureCeiling = full[nameof(RotationBackoffOptions.FailureCeiling)];
    }

    public CollectionSettings Read()
    {
        CollectionSettings unset = new();

        return new CollectionSettings
        {
            BetweenSweeps = Positive(BetweenSweeps, nameof(BetweenSweeps), unset.BetweenSweeps),
            WantedCoverage = Positive(WantedCoverage, nameof(WantedCoverage), unset.WantedCoverage),
            RevisitsBelow = Positive(RevisitsBelow, nameof(RevisitsBelow), unset.RevisitsBelow),
            BetweenVisits = Waited(BetweenVisits, nameof(BetweenVisits), unset.BetweenVisits),
            BeforeRetrying = Waited(BeforeRetrying, nameof(BeforeRetrying), unset.BeforeRetrying),
            LongestVisit = Positive(LongestVisit, nameof(LongestVisit), unset.LongestVisit),
            KeepEndedProgrammes = Waited(
                KeepEndedProgrammes, nameof(KeepEndedProgrammes), unset.KeepEndedProgrammes),
            ArchiveRetention = Kept(ArchiveRetention, nameof(ArchiveRetention), unset.ArchiveRetention),
            LongestBackOff = Waited(LongestBackOff, nameof(LongestBackOff), unset.LongestBackOff),
            BetweenBoosts = Waited(BetweenBoosts, nameof(BetweenBoosts), unset.BetweenBoosts),
            LongestBoost = Waited(LongestBoost, nameof(LongestBoost), unset.LongestBoost),
            RidesAlong = Either(RidesAlong, nameof(RidesAlong), unset.RidesAlong),
            BetweenRideAlongSaves = Positive(
                BetweenRideAlongSaves, nameof(BetweenRideAlongSaves), unset.BetweenRideAlongSaves),
            BetweenSessionChecks = Positive(
                BetweenSessionChecks, nameof(BetweenSessionChecks), unset.BetweenSessionChecks),
            WhenTunersAreFull = Backing(unset.WhenTunersAreFull),
        };
    }

    private RotationBackoff Backing(RotationBackoff unset)
    {
        TimeSpan firstDelay = Positive(
            WhenTunersAreFull.FirstDelay,
            $"{nameof(WhenTunersAreFull)}:{nameof(RotationBackoffOptions.FirstDelay)}",
            unset.FirstDelay);
        int factor = Counted(
            WhenTunersAreFull.Factor,
            $"{nameof(WhenTunersAreFull)}:{nameof(RotationBackoffOptions.Factor)}",
            unset.Factor);
        TimeSpan maximumDelay = Positive(
            WhenTunersAreFull.MaximumDelay,
            $"{nameof(WhenTunersAreFull)}:{nameof(RotationBackoffOptions.MaximumDelay)}",
            unset.MaximumDelay);
        int failureCeiling = Counted(
            WhenTunersAreFull.FailureCeiling,
            $"{nameof(WhenTunersAreFull)}:{nameof(RotationBackoffOptions.FailureCeiling)}",
            unset.FailureCeiling);

        try
        {
            return new RotationBackoff(firstDelay, factor, maximumDelay, failureCeiling);
        }
        catch (ArgumentOutOfRangeException refusal)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(WhenTunersAreFull)} does not describe a back-off: {refusal.Message}",
                nameof(WhenTunersAreFull),
                refusal);
        }
    }

    private static TimeSpan Positive(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        TimeSpan read = Duration(setting, name);

        return read > TimeSpan.Zero
            ? read
            : throw new ArgumentException($"{Section}:{name} has to be longer than nothing.", name);
    }

    private static TimeSpan Waited(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        TimeSpan read = Duration(setting, name);

        return read >= TimeSpan.Zero
            ? read
            : throw new ArgumentException($"{Section}:{name} cannot be a negative wait.", name);
    }

    private static TimeSpan? Kept(string? setting, string name, TimeSpan? unset)
        => string.IsNullOrWhiteSpace(setting) ? unset : Positive(setting, name, TimeSpan.Zero);

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

    private static int Counted(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int read)
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} reads a whole number, and '{setting}' is not one.",
                name);
    }

    private static TimeSpan Duration(string setting, string name)
        => TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan read)
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} reads a duration as [d.]hh:mm:ss, and '{setting}' is not one.",
                name);
}

public sealed class CollectionValidation : IValidateOptions<CollectionOptions>
{
    public ValidateOptionsResult Validate(string? name, CollectionOptions options)
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
