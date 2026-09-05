using System.Globalization;

using Carina.Domain.Encodings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class EncodingOptions
{
    public const string Section = "Encodings";

    public string? WorkedIn { get; set; }

    public string? Prefer { get; set; }

    public string? MostCores { get; set; }

    public string? MostAttempts { get; set; }

    public string? BetweenLooks { get; set; }

    public string? StalledAfter { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        WorkedIn = named[nameof(WorkedIn)];
        Prefer = named[nameof(Prefer)];
        MostCores = named[nameof(MostCores)];
        MostAttempts = named[nameof(MostAttempts)];
        BetweenLooks = named[nameof(BetweenLooks)];
        StalledAfter = named[nameof(StalledAfter)];
    }

    public EncodeSettings Read()
    {
        EncodeSettings unset = new();

        return new EncodeSettings
        {
            WorkedIn = Absolute(WorkedIn, nameof(WorkedIn)),
            Prefer = Named(Prefer, nameof(Prefer), unset.Prefer),
            MostCores = Counted(MostCores, nameof(MostCores), unset.MostCores, "cores"),
            MostAttempts = Counted(MostAttempts, nameof(MostAttempts), unset.MostAttempts, "attempts"),
            BetweenLooks = Timed(BetweenLooks, nameof(BetweenLooks), unset.BetweenLooks),
            StalledAfter = Timed(StalledAfter, nameof(StalledAfter), unset.StalledAfter),
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

    private static EncodeEncoder Named(string? setting, string name, EncodeEncoder unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return Enum.TryParse(setting, ignoreCase: true, out EncodeEncoder named) && Enum.IsDefined(named)
            ? named
            : throw new ArgumentException(
                $"{Section}:{name} is one of {string.Join(", ", Enum.GetNames<EncodeEncoder>())}.",
                name);
    }

    private static int Counted(string? setting, string name, int unset, string of)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int counted) && counted >= 1
            ? counted
            : throw new ArgumentException($"{Section}:{name} is a whole number of {of}, at least 1.", name);
    }

    private static TimeSpan Timed(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan timed) && timed > TimeSpan.Zero
            ? timed
            : throw new ArgumentException($"{Section}:{name} is a positive length of time such as 00:10:00.", name);
    }
}

public sealed class EncodingValidation : IValidateOptions<EncodingOptions>
{
    public ValidateOptionsResult Validate(string? name, EncodingOptions options)
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
