namespace Carina.Infrastructure.Machines;

public static class NvencProbeInvocation
{
    public static IReadOnlyList<string> Arguments()
        => Arguments(FfmpegFaculties.H264OnNvenc);

    public static IReadOnlyList<string> Arguments(string encoder)
    {
        ArgumentException.ThrowIfNullOrEmpty(encoder);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-init_hw_device",
            CudaDevice.Made,
            "-filter_hw_device",
            CudaDevice.Name,
            "-f",
            "lavfi",
            "-i",
            VaapiProbeInvocation.Picture,
            "-vf",
            "format=nv12,hwupload_cuda",
            "-c:v",
            encoder,
            "-f",
            "null",
            "-",
        ];
    }
}
