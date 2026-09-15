using System.Globalization;

using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What the two runs said, read line by line as it comes. The quiet and the dark are written on
/// the error stream because that is where the filters that find them talk, and the score for how
/// much the picture changed is written on the output stream because that is where the filter that
/// prints it was told to write; the two are kept apart here for that reason and no other.
/// <para>
/// The words are ffmpeg's own and are no interface it promised to keep, so this is held to the
/// tool by a test that runs the real one rather than by reading its source. Every moment is kept
/// exactly as reported, on whatever clock it was reported on: putting it somewhere is
/// <see cref="ChapterClock"/>'s to do, not this. A quiet stretch the run never said the end of —
/// the source having stopped in the middle of it — is not one, and is left out.
/// </para>
/// </summary>
public sealed class ChapterLog
{
    public const string QuietFrom = "silence_start:";

    public const string QuietUntil = "silence_end:";

    public const string DarkFrom = "black_start:";

    public const string DarkUntil = "black_end:";

    public const string Pictured = "pts_time:";

    public const string Changed = "lavfi.scene_score=";

    private readonly List<ChapterSpan> silences = [];

    private readonly List<ChapterSpan> blacks = [];

    private readonly List<ChapterScene> scenes = [];

    private TimeSpan? wentQuiet;

    private TimeSpan? pictured;

    public IReadOnlyList<ChapterSpan> Silences => silences;

    public IReadOnlyList<ChapterSpan> Blacks => blacks;

    public IReadOnlyList<ChapterScene> Scenes => scenes;

    public void Complained(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Moment(line, QuietFrom) is { } quiet)
        {
            wentQuiet = quiet;
        }

        if (Moment(line, QuietUntil) is { } spoke)
        {
            if (wentQuiet is { } began && spoke > began)
            {
                silences.Add(new ChapterSpan(began, spoke));
            }

            wentQuiet = null;
        }

        if (Moment(line, DarkFrom) is { } dark && Moment(line, DarkUntil) is { } lit && lit > dark)
        {
            blacks.Add(new ChapterSpan(dark, lit));
        }
    }

    public void Said(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Moment(line, Pictured) is { } frame)
        {
            pictured = frame;
        }

        if (Scored(line) is { } score && pictured is { } shown)
        {
            scenes.Add(new ChapterScene(shown, score));
        }
    }

    internal static TimeSpan? Moment(string line, string named)
        => Number(line, named) is { } seconds && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;

    private static double? Scored(string line)
        => Number(line, Changed) is { } score && score >= 0 ? score : null;

    private static double? Number(string line, string named)
    {
        int found = line.IndexOf(named, StringComparison.Ordinal);

        if (found < 0)
        {
            return null;
        }

        int from = found + named.Length;

        while (from < line.Length && line[from] is ' ')
        {
            from++;
        }

        int until = from;

        while (until < line.Length && !char.IsWhiteSpace(line[until]))
        {
            until++;
        }

        return double.TryParse(
            line.AsSpan(from, until - from),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double read) && double.IsFinite(read)
            ? read
            : null;
    }
}
