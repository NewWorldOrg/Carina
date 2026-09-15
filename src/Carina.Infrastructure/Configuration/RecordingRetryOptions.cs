using System.Globalization;

using Carina.Domain.Recordings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class RecordingRetryOptions
{
    public const string Section = "RecordingRetry";

    public string? MostAttempts { get; set; }

    public string? BetweenAttempts { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        MostAttempts = named[nameof(MostAttempts)];
        BetweenAttempts = named[nameof(BetweenAttempts)];
    }

    public RetryPolicy Read()
    {
        RetryPolicy unset = RetryPolicy.Default;

        try
        {
            return new RetryPolicy(
                Count(MostAttempts, nameof(MostAttempts), unset.MostAttempts),
                Duration(BetweenAttempts, nameof(BetweenAttempts), unset.BetweenAttempts));
        }
        catch (ArgumentOutOfRangeException refusal)
        {
            throw new ArgumentException(
                $"{Section} does not describe a retry the recorder can hold: {refusal.Message}",
                refusal.ParamName,
                refusal);
        }
    }

    private static int Count(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return int.TryParse(setting.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"{Section}:{name} reads a whole number, and '{setting}' is not one.", name);
    }

    private static TimeSpan Duration(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a duration as [d.]hh:mm:ss, and '{setting}' is not one.",
                name);
    }
}

public sealed class RecordingRetryValidation : IValidateOptions<RecordingRetryOptions>
{
    public ValidateOptionsResult Validate(string? name, RecordingRetryOptions options)
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
