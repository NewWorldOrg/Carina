using System.Globalization;

using Carina.Domain.Integrity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class IntegrityOptions
{
    public const string Section = "Integrity";

    public string? BeforeFirstSweep { get; set; }

    public string? BetweenSweeps { get; set; }

    public string? BetweenManualSweeps { get; set; }

    public string? OutputRoots { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BeforeFirstSweep = named[nameof(BeforeFirstSweep)];
        BetweenSweeps = named[nameof(BetweenSweeps)];
        BetweenManualSweeps = named[nameof(BetweenManualSweeps)];
        OutputRoots = named[nameof(OutputRoots)];
    }

    public IntegritySettings Read()
    {
        IntegritySettings unset = new();

        return new IntegritySettings
        {
            BeforeFirstSweep = Positive(BeforeFirstSweep, nameof(BeforeFirstSweep), unset.BeforeFirstSweep),
            BetweenSweeps = Positive(BetweenSweeps, nameof(BetweenSweeps), unset.BetweenSweeps),
            BetweenManualSweeps = Positive(
                BetweenManualSweeps,
                nameof(BetweenManualSweeps),
                unset.BetweenManualSweeps),
            OutputRoots = Mounted(),
        };
    }

    private IReadOnlyList<StorageRootPath> Mounted() => MountedRoots.Read(Section, nameof(OutputRoots), OutputRoots);

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

    private static string Absolute(string? setting, string name, string unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return setting.StartsWith('/')
            ? setting
            : throw new ArgumentException(
                $"{Section}:{name} is written where the process can reach it, and '{setting}' is not absolute.",
                name);
    }
}

public sealed class IntegrityValidation : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
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
