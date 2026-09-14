using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class EncodingOptions
{
    public const string Section = "Encodings";

    public string? OutputRoots { get; set; }

    public string? WorkedIn { get; set; }

    public string? Automatically { get; set; }

    public string? Prefer { get; set; }

    public string? MostCores { get; set; }

    public string? MostAttempts { get; set; }

    public string? BetweenLooks { get; set; }

    public string? StalledAfter { get; set; }

    public EncodingChapterOptions Chapters { get; set; } = new();

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        OutputRoots = named[nameof(OutputRoots)];
        WorkedIn = named[nameof(WorkedIn)];
        Automatically = named[nameof(Automatically)];
        Prefer = named[nameof(Prefer)];
        MostCores = named[nameof(MostCores)];
        MostAttempts = named[nameof(MostAttempts)];
        BetweenLooks = named[nameof(BetweenLooks)];
        StalledAfter = named[nameof(StalledAfter)];
        Chapters.ReadFrom(named.GetSection(nameof(Chapters)));
    }

    public EncodeSettings Read()
    {
        EncodeSettings unset = new();

        return new EncodeSettings
        {
            OutputRoots = Held(),
            WorkedIn = Absolute(WorkedIn, nameof(WorkedIn)),
            Automatically = Told(Automatically, nameof(Automatically), unset.Automatically),
            Prefer = Named(Prefer, nameof(Prefer), unset.Prefer),
            MostCores = Counted(MostCores, nameof(MostCores), unset.MostCores, "cores"),
            MostAttempts = Counted(MostAttempts, nameof(MostAttempts), unset.MostAttempts, "attempts"),
            BetweenLooks = Timed(BetweenLooks, nameof(BetweenLooks), unset.BetweenLooks),
            StalledAfter = Timed(StalledAfter, nameof(StalledAfter), unset.StalledAfter),
            Chapters = Chapters.Read(),
        };
    }

    private IReadOnlyList<StorageRootPath> Held() => MountedRoots.Read(Section, nameof(OutputRoots), OutputRoots);

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

    private static bool Told(string? setting, string name, bool unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return bool.TryParse(setting, out bool told)
            ? told
            : throw new ArgumentException($"{Section}:{name} is either true or false.", name);
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

public sealed class EncodingChapterOptions
{
    public const string Section = EncodingOptions.Section + ":Chapters";

    private const int Quietest = -100;

    private const int Loudest = -1;

    public string? Marked { get; set; }

    public string? Noise { get; set; }

    public string? ShortestSilence { get; set; }

    public string? Scene { get; set; }

    public string? Grid { get; set; }

    public string? GridTolerance { get; set; }

    public string? MostBreakShare { get; set; }

    public string? MostChapters { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Marked = configuration[nameof(Marked)];
        Noise = configuration[nameof(Noise)];
        ShortestSilence = configuration[nameof(ShortestSilence)];
        Scene = configuration[nameof(Scene)];
        Grid = configuration[nameof(Grid)];
        GridTolerance = configuration[nameof(GridTolerance)];
        MostBreakShare = configuration[nameof(MostBreakShare)];
        MostChapters = configuration[nameof(MostChapters)];
    }

    public ChapterSettings Read()
    {
        ChapterSettings unset = new();
        TimeSpan grid = Timed(Grid, nameof(Grid), unset.Grid);

        return new ChapterSettings
        {
            Marked = Told(Marked, nameof(Marked), unset.Marked),
            Noise = Quietened(Noise, nameof(Noise), unset.Noise),
            ShortestSilence = Timed(ShortestSilence, nameof(ShortestSilence), unset.ShortestSilence),
            Scene = Shared(Scene, nameof(Scene), unset.Scene),
            Grid = grid,
            GridTolerance = Nearer(Timed(GridTolerance, nameof(GridTolerance), unset.GridTolerance), grid),
            MostBreakShare = Shared(MostBreakShare, nameof(MostBreakShare), unset.MostBreakShare),
            MostChapters = Counted(MostChapters, nameof(MostChapters), unset.MostChapters),
        };
    }

    private static bool Told(string? setting, string name, bool unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return bool.TryParse(setting, out bool told)
            ? told
            : throw new ArgumentException($"{Section}:{name} is either true or false.", name);
    }

    private static int Quietened(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quietened)
            && quietened is >= Quietest and <= Loudest
            ? quietened
            : throw new ArgumentException(
                $"{Section}:{name} is a whole number of decibels under silence, from {Quietest} to {Loudest}.",
                name);
    }

    private static double Shared(string? setting, string name, double unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return double.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out double shared)
            && shared > 0
            && shared <= 1
            ? shared
            : throw new ArgumentException($"{Section}:{name} is a share of the whole, above 0 and at most 1.", name);
    }

    private static int Counted(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int counted) && counted >= 1
            ? counted
            : throw new ArgumentException($"{Section}:{name} is a whole number of marks, at least 1.", name);
    }

    private static TimeSpan Timed(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan timed) && timed > TimeSpan.Zero
            ? timed
            : throw new ArgumentException($"{Section}:{name} is a positive length of time such as 00:00:15.", name);
    }

    private static TimeSpan Nearer(TimeSpan tolerance, TimeSpan grid)
        => tolerance < grid / 2
            ? tolerance
            : throw new ArgumentException(
                $"{Section}:{nameof(GridTolerance)} is less than half of {Section}:{nameof(Grid)}, or every pair sits on the grid.",
                nameof(GridTolerance));
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
