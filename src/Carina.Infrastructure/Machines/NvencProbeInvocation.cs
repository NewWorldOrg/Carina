namespace Carina.Infrastructure.Machines;

/// <summary>
/// Asks whether an NVIDIA card encodes a frame: the same black picture the VAAPI probe sends, uploaded to
/// the CUDA device and encoded with an NVENC encoder. The render node is never opened; a card that cannot
/// be reached fails to open the CUDA device and the probe exits with its complaint.
/// </summary>
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
