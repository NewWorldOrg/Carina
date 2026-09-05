namespace Carina.Infrastructure.Machines;

public static class VaapiProbeInvocation
{
    public const string Picture = "color=black:s=64x64:r=1:d=1";

    public static IReadOnlyList<string> Arguments(string renderNode)
        => Arguments(renderNode, FfmpegFaculties.H264OnTheCard);

    public static IReadOnlyList<string> Arguments(string renderNode, string encoder)
    {
        ArgumentException.ThrowIfNullOrEmpty(renderNode);
        ArgumentException.ThrowIfNullOrEmpty(encoder);

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
            encoder,
            "-f",
            "null",
            "-",
        ];
    }
}
