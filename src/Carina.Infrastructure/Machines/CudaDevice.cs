namespace Carina.Infrastructure.Machines;

/// <summary>
/// The CUDA device every command that encodes on an NVIDIA card opens: made once under one name, and
/// handed to the filters by that name. No path is involved; the NVIDIA container runtime decides which
/// card answers, so there is nothing here for a setting to choose.
/// </summary>
public static class CudaDevice
{
    public const string Made = "cuda=cu";

    public const string Name = "cu";
}
