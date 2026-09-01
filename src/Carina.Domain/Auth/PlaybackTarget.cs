using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public enum PlaybackTargetKind
{
    Recording = 1,

    LiveChannel = 2,
}

public sealed class PlaybackTarget : CommonValueObject<string>
{
    public const int LongestName = 128;

    public const char Separator = '/';

    private PlaybackTarget(PlaybackTargetKind kind, string name)
        : base($"{Word(kind)}{Separator}{name}")
    {
        Kind = kind;
        Name = name;
    }

    public PlaybackTargetKind Kind { get; }

    public string Name { get; }

    public static PlaybackTarget Recording(string name)
        => new(PlaybackTargetKind.Recording, Validated(name));

    public static PlaybackTarget LiveChannel(string name)
        => new(PlaybackTargetKind.LiveChannel, Validated(name));

    private static string Word(PlaybackTargetKind kind)
        => kind is PlaybackTargetKind.Recording ? "recording" : "live-channel";

    private static string Validated(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A ticket says what may be watched, so the target names something.",
                nameof(name));
        }

        if (name.Length > LongestName)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name.Length,
                $"A target name is at most {LongestName} characters.");
        }

        if (name != name.Trim())
        {
            throw new ArgumentException(
                "A target is compared against the one a caller asks for, and padding would make two names for one thing.",
                nameof(name));
        }

        if (name.Contains(Separator, StringComparison.Ordinal) || name.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A target reads as one kind and one name joined by '{Separator}', so the name carries neither.",
                nameof(name));
        }

        return name;
    }
}
