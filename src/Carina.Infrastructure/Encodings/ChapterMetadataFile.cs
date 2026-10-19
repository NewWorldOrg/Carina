using System.Globalization;
using System.Text;

using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Writes the chapters of one artefact in the shape ffmpeg reads chapters from. Every moment goes
/// down in whole milliseconds on the clock <see cref="ChapterClock.InTheMetadata"/> puts it on, which is
/// the recording's own time, and every chapter is titled from the constant for its kind.
/// </summary>
public static class ChapterMetadataFile
{
    public const string Header = ";FFMETADATA1";

    public const string Opening = "[CHAPTER]";

    public const string TimeBase = "TIMEBASE=1/1000";

    public const string ProgrammeTitle = "本編";

    public const string BreakTitle = "CM";

    public const string OpeningTitle = "OP";

    public const string EndingTitle = "ED";

    private static readonly UTF8Encoding Utf8 = new(false);

    public static string Written(IReadOnlyList<ChapterSegment> segments, TimeSpan headSkip)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return Lines([.. segments.Select(segment => (
            ChapterClock.InTheMetadata(segment.Starts, headSkip),
            ChapterClock.InTheMetadata(segment.Ends, headSkip),
            TitleOf(segment.Kind)))]);
    }

    /// <summary>
    /// The stretches of a recording as chapters, written in the recording's own time as they are.
    /// </summary>
    public static string InRecordingTime(IReadOnlyList<SegmentSpan> stretches)
    {
        ArgumentNullException.ThrowIfNull(stretches);

        return Lines([.. stretches.Select(stretch => (stretch.Starts, stretch.Ends, TitleOf(EncodeChapterChoice.KindOf(stretch.Kind))))]);
    }

    public static Task WriteAsync(
        string path,
        IReadOnlyList<ChapterSegment> segments,
        TimeSpan headSkip,
        CancellationToken cancellationToken)
        => WriteAsync(path, Written(segments, headSkip), cancellationToken);

    public static Task WriteAsync(string path, string written, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(written);

        return File.WriteAllTextAsync(path, written, Utf8, cancellationToken);
    }

    private static string Lines(IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)> chapters)
    {
        if (chapters.Count is 0)
        {
            throw new ArgumentException("A chapters file is written for chapters, and there are none.", nameof(chapters));
        }

        var written = new StringBuilder();
        written.Append(Header).Append('\n');

        foreach ((TimeSpan starts, TimeSpan ends, string title) in chapters)
        {
            written.Append(Opening).Append('\n');
            written.Append(TimeBase).Append('\n');
            written.Append("START=").Append(Milliseconds(starts)).Append('\n');
            written.Append("END=").Append(Milliseconds(ends)).Append('\n');
            written.Append("title=").Append(title).Append('\n');
        }

        return written.ToString();
    }

    /// <summary>
    /// The title a chapter of <paramref name="kind"/> goes down with.
    /// </summary>
    public static string TitleOf(ChapterKind kind) => kind switch
    {
        ChapterKind.Programme => ProgrammeTitle,
        ChapterKind.Break => BreakTitle,
        ChapterKind.Opening => OpeningTitle,
        ChapterKind.Ending => EndingTitle,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A chapter is titled for one of the kinds named."),
    };

    private static string Milliseconds(TimeSpan inTheMetadata)
        => ((long)Math.Round(inTheMetadata.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);
}
