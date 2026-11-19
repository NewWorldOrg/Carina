namespace Carina.Infrastructure.Encodings;

public static class FfmpegCaptionTrackInvocation
{
    public const string Codec = "mov_text";

    public const string Language = "jpn";

    public const string LanguageNamed = "language=jpn";

    public const string Entries = "stream=codec_type,codec_name:stream_tags=language:stream_disposition=default:format=duration";

    public static IReadOnlyList<string> Arguments(string artefact, string captions, string destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(artefact);
        ArgumentException.ThrowIfNullOrEmpty(captions);
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
            "-f",
            "webvtt",
            "-i",
            captions,
            "-map",
            "0:v",
            "-map",
            "0:a?",
            "-map",
            "1:0",
            "-map_chapters",
            "0",
            "-map_metadata",
            "0",
            "-c",
            "copy",
            "-c:s",
            Codec,
            "-metadata:s:s:0",
            LanguageNamed,
            "-disposition:s:0",
            "0",
            "-f",
            "mp4",
            "-movflags",
            "faststart",
            destination,
        ];
    }

    public static IReadOnlyList<string> Carried(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            "json",
            "-show_entries",
            Entries,
            "-i",
            file,
        ];
    }
}
