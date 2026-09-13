using System.Globalization;

using Carina.Domain.Programmes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class ProgrammeFeedOptions
{
    public const string Section = "ProgrammeFeed";

    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(int.MaxValue);

    public string? ConcurrentReaders { get; set; }

    public string? StatementTimeout { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        ConcurrentReaders = named[nameof(ConcurrentReaders)];
        StatementTimeout = named[nameof(StatementTimeout)];
    }

    public ProgrammeFeedSettings Read()
    {
        ProgrammeFeedSettings unset = new();

        return new ProgrammeFeedSettings
        {
            ConcurrentReaders = Counted(
                ConcurrentReaders, nameof(ConcurrentReaders), unset.ConcurrentReaders),
            StatementTimeout = Positive(StatementTimeout, nameof(StatementTimeout), unset.StatementTimeout),
        };
    }

    private static int Counted(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        int read = int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a whole number, and '{setting}' is not one.",
                name);

        return read > 0
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} has to leave at least one caller through.",
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

        if (read <= TimeSpan.Zero)
        {
            throw new ArgumentException($"{Section}:{name} has to be longer than nothing.", name);
        }

        return read <= Longest
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} cannot be longer than {Longest}, "
                + "which is as many milliseconds as a statement_timeout counts.",
                name);
    }
}

public sealed class ProgrammeFeedValidation : IValidateOptions<ProgrammeFeedOptions>
{
    public ValidateOptionsResult Validate(string? name, ProgrammeFeedOptions options)
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
