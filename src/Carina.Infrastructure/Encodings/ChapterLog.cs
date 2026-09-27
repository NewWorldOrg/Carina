using System.Globalization;

using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads what the two runs said, line by line as it comes: the quiet and the dark from the error
/// stream, and the score for how much the picture changed from the output stream.
/// </summary>
/// <remarks>
/// Every moment is kept exactly as reported, on whatever clock it was reported on. A quiet stretch
/// whose end the run never reported is left out.
/// </remarks>
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
