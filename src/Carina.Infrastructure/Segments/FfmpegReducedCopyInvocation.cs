namespace Carina.Infrastructure.Segments;

/// <summary>
/// The arguments for the run that decodes a reduced copy into what its learning data is made from, and
/// nothing more: its two channels put back from their sum and their difference at 8 kHz in two channels of
/// 16-bit samples, its frames in grey at every frame, and its tiles of the corners in grey at the first
/// frame of each second, neither resized. All three go into Matroska on the standard output, every block
/// carrying the copy's own time. Each file is named as a file, so no name is read as a protocol. One thread
/// a decoder and for the filters, and no card.
/// </summary>
public static class FfmpegReducedCopyInvocation
{
    public const string HandedBack = "pipe:1";

    public static IReadOnlyList<string> Arguments(ReducedCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "warning",
            "-nostats",
            .. Input(copy, ReducedCopy.Sum),
            .. Input(copy, ReducedCopy.Difference),
            .. Input(copy, ReducedCopy.Frames),
            .. Input(copy, ReducedCopy.Corners),
            "-filter_threads",
            "1",
            "-filter_complex_threads",
            "1",
            "-copyts",
            "-filter_complex",
            "[2:v:0]format=gray[f];[3:v:0]select=isnan(prev_selected_t)+gte(floor(t)\\,floor(prev_selected_t)+1),format=gray[c];[0:a:0][1:a:0]join=inputs=2:channel_layout=stereo:map=0.0-FL|1.0-FR,pan=stereo|c0=c0+c1|c1=c0-c1,aresample=8000,aformat=sample_fmts=s16:channel_layouts=stereo[a]",
            "-map",
            "[f]",
            "-map",
            "[c]",
            "-map",
            "[a]",
            "-c:v",
            "rawvideo",
            "-c:a",
            "pcm_s16le",
            "-fps_mode",
            "passthrough",
            "-f",
            "matroska",
            HandedBack,
        ];
    }

    private static string[] Input(ReducedCopy copy, string file) => ["-threads", "1", "-i", $"file:{copy.PathOf(file)}"];
}
