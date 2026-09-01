namespace Carina.Infrastructure.Streaming;

public static class VaapiProbeInvocation
{
    public const string Picture = "color=black:s=64x64:r=1:d=1";

    public static IReadOnlyList<string> Arguments()
        =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-vaapi_device",
            FfmpegLiveInvocation.RenderNode,
            "-f",
            "lavfi",
            "-i",
            Picture,
            "-vf",
            "format=nv12,hwupload",
            "-c:v",
            "h264_vaapi",
            "-f",
            "null",
            "-",
        ];
}
