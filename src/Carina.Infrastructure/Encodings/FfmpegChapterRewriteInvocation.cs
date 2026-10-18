namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Copies an artefact's picture, sound and subtitle tracks and what it says about itself into a new file with
/// the chapters read from a metadata file in place of its own, or with no chapters when there is none to read;
/// and reads back the chapters a file carries.
/// </summary>
public static class FfmpegChapterRewriteInvocation
{
    public static IReadOnlyList<string> Arguments(string artefact, string? chapters, string destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(artefact);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            artefact,
            .. FfmpegEncodeInvocation.ReadingChapters(chapters),
            "-map",
            "0:v",
            "-map",
            "0:a?",
            "-map",
            "0:s?",
            "-map_chapters",
            string.IsNullOrEmpty(chapters) ? "-1" : FfmpegEncodeInvocation.ChaptersInput,
            "-map_metadata",
            "0",
            "-c",
            "copy",
            "-f",
            "mp4",
            "-movflags",
            "faststart",
            destination,
        ];
    }

    public static IReadOnlyList<string> Chapters(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            "json",
            "-show_chapters",
            "-i",
            file,
        ];
    }
}
