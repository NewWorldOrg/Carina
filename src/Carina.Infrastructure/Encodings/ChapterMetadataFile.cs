using System.Globalization;
using System.Text;

using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Writes the chapters of one artefact in the shape ffmpeg reads chapters from. Every moment goes
/// down in whole milliseconds on the clock <see cref="ChapterClock.InTheMetadata"/> puts it on, and
/// every chapter is titled from two constants.
/// </summary>
public static class ChapterMetadataFile
{
    public const string Header = ";FFMETADATA1";

    public const string Opening = "[CHAPTER]";

    public const string TimeBase = "TIMEBASE=1/1000";

    public const string ProgrammeTitle = "本編";

    public const string BreakTitle = "CM";

    public static string Written(IReadOnlyList<ChapterSegment> segments, TimeSpan headSkip)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count is 0)
        {
            throw new ArgumentException("A chapters file is written for chapters, and there are none.", nameof(segments));
        }

        var written = new StringBuilder();
        written.Append(Header).Append('\n');

        foreach (ChapterSegment segment in segments)
        {
            written.Append(Opening).Append('\n');
            written.Append(TimeBase).Append('\n');
            written.Append("START=").Append(Milliseconds(segment.Starts, headSkip)).Append('\n');
            written.Append("END=").Append(Milliseconds(segment.Ends, headSkip)).Append('\n');
            written.Append("title=").Append(Titled(segment.Kind)).Append('\n');
        }

        return written.ToString();
    }

    public static Task WriteAsync(
        string path,
        IReadOnlyList<ChapterSegment> segments,
        TimeSpan headSkip,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return File.WriteAllTextAsync(path, Written(segments, headSkip), new UTF8Encoding(false), cancellationToken);
    }

    private static string Titled(ChapterKind kind) => kind is ChapterKind.Break ? BreakTitle : ProgrammeTitle;

    private static string Milliseconds(TimeSpan onTheArtefact, TimeSpan headSkip)
        => ((long)Math.Round(ChapterClock.InTheMetadata(onTheArtefact, headSkip).TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture);
}
