using System.Globalization;

using Carina.Domain.Thumbnails;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class ThumbnailOptions
{
    public const string Section = "Thumbnails";

    public string? WrittenTo { get; set; }

    public string? Programme { get; set; }

    public string? BeforeFirstPass { get; set; }

    public string? BetweenPasses { get; set; }

    public string? NoLaterThan { get; set; }

    public string? OneOverAShareOf { get; set; }

    public string? LongestRender { get; set; }

    public string? AtMostAPass { get; set; }

    public string? Width { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        WrittenTo = named[nameof(WrittenTo)];
        Programme = named[nameof(Programme)];
        BeforeFirstPass = named[nameof(BeforeFirstPass)];
        BetweenPasses = named[nameof(BetweenPasses)];
        NoLaterThan = named[nameof(NoLaterThan)];
        OneOverAShareOf = named[nameof(OneOverAShareOf)];
        LongestRender = named[nameof(LongestRender)];
        AtMostAPass = named[nameof(AtMostAPass)];
        Width = named[nameof(Width)];
    }

    public ThumbnailSettings Read()
    {
        ThumbnailSettings unset = new();

        return new ThumbnailSettings
        {
            WrittenTo = Absolute(WrittenTo, nameof(WrittenTo)),
            Programme = Named(Programme, nameof(Programme), unset.Programme),
            BeforeFirstPass = Positive(BeforeFirstPass, nameof(BeforeFirstPass), unset.BeforeFirstPass),
            BetweenPasses = Positive(BetweenPasses, nameof(BetweenPasses), unset.BetweenPasses),
            NoLaterThan = Positive(NoLaterThan, nameof(NoLaterThan), unset.NoLaterThan),
            OneOverAShareOf = Counted(OneOverAShareOf, nameof(OneOverAShareOf), unset.OneOverAShareOf, 1),
            LongestRender = Positive(LongestRender, nameof(LongestRender), unset.LongestRender),
            AtMostAPass = Counted(AtMostAPass, nameof(AtMostAPass), unset.AtMostAPass, 1),
            Width = Even(Width, nameof(Width), unset.Width),
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

    private static string Named(string? setting, string name, string unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return setting.Trim() == setting
            ? setting
            : throw new ArgumentException($"{Section}:{name} carries no surrounding space.", name);
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

    private static int Counted(string? setting, string name, int unset, int lowest)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        int read = int.TryParse(setting, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"{Section}:{name} reads a whole number, and '{setting}' is not one.", name);

        return read >= lowest
            ? read
            : throw new ArgumentException($"{Section}:{name} is at least {lowest}, and '{setting}' is not.", name);
    }

    private static int Even(string? setting, string name, int unset)
    {
        int read = Counted(setting, name, unset, 2);

        return read % 2 is 0
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} is an even number of pixels, and '{setting}' is not.",
                name);
    }
}

public sealed class ThumbnailValidation : IValidateOptions<ThumbnailOptions>
{
    public ValidateOptionsResult Validate(string? name, ThumbnailOptions options)
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
