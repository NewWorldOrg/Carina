using System.Globalization;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class IntegrityOptions
{
    public const string Section = "Integrity";

    private const char BetweenRoots = ';';

    private const char BetweenNameAndPath = '=';

    public string? BeforeFirstSweep { get; set; }

    public string? BetweenSweeps { get; set; }

    public string? OutputRoots { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BeforeFirstSweep = named[nameof(BeforeFirstSweep)];
        BetweenSweeps = named[nameof(BetweenSweeps)];
        OutputRoots = named[nameof(OutputRoots)];
    }

    public IntegritySettings Read()
    {
        IntegritySettings unset = new();

        return new IntegritySettings
        {
            BeforeFirstSweep = Positive(BeforeFirstSweep, nameof(BeforeFirstSweep), unset.BeforeFirstSweep),
            BetweenSweeps = Positive(BetweenSweeps, nameof(BetweenSweeps), unset.BetweenSweeps),
            OutputRoots = Mounted(),
        };
    }

    private IReadOnlyList<StorageRootPath> Mounted()
    {
        if (string.IsNullOrWhiteSpace(OutputRoots))
        {
            return [];
        }

        List<StorageRootPath> mounted = [];
        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (string entry in OutputRoots.Split(BetweenRoots, StringSplitOptions.TrimEntries))
        {
            if (entry.Length is 0)
            {
                continue;
            }

            int split = entry.IndexOf(BetweenNameAndPath, StringComparison.Ordinal);

            if (split < 0)
            {
                throw new ArgumentException(
                    $"{Section}:{nameof(OutputRoots)} reads a ';'-separated list of name=/path, "
                    + $"and '{entry}' names no path.",
                    nameof(OutputRoots));
            }

            StorageRootPath read = Mounted(
                entry[..split].Trim(),
                entry[(split + 1)..].Trim());

            if (!named.Add(read.Root.Value))
            {
                throw new ArgumentException(
                    $"{Section}:{nameof(OutputRoots)} mounts '{read.Root.Value}' twice, "
                    + "so which path it means is unanswerable.",
                    nameof(OutputRoots));
            }

            mounted.Add(read);
        }

        return mounted;
    }

    private static StorageRootPath Mounted(string name, string path)
    {
        try
        {
            return new StorageRootPath(new OutputRoot(name), path);
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(OutputRoots)} does not describe a mounted output root: {refusal.Message}",
                nameof(OutputRoots),
                refusal);
        }
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
