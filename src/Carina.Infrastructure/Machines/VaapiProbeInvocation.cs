namespace Carina.Infrastructure.Machines;

public static class VaapiProbeInvocation
{
    public const string Picture = "color=black:s=64x64:r=1:d=1";

    public static IReadOnlyList<string> Arguments(string renderNode)
    {
        ArgumentException.ThrowIfNullOrEmpty(renderNode);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-vaapi_device",
            renderNode,
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
}
