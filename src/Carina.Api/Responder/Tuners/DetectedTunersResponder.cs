using Carina.Api.Services;
using Carina.Contracts;

namespace Carina.Api.Responder.Tuners;

public sealed record DetectedDeviceResponder(
    string DeviceId,
    DeviceDetection Detection,
    IReadOnlyList<TunerKind> Kinds,
    string? Detail)
{
    public static DetectedDeviceResponder Of(DetectedDeviceDto device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DetectedDeviceResponder(device.DeviceId, device.Detection, device.Kinds, device.Detail);
    }
}

public sealed record TunerKindMismatchResponder(
    string DeviceId,
    TunerKind Observed,
    IReadOnlyList<TunerKind> Detected)
{
    public static TunerKindMismatchResponder Of(TunerKindMismatch mismatch)
    {
        ArgumentNullException.ThrowIfNull(mismatch);

        return new TunerKindMismatchResponder(mismatch.DeviceId, mismatch.Observed, mismatch.Detected);
    }
}

public sealed record DetectedTunersResponder(
    IReadOnlyList<DetectedDeviceResponder> Devices,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Missing,
    IReadOnlyList<TunerKindMismatchResponder> Mismatched)
{
    public static DetectedTunersResponder Of(DetectedTunersView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new DetectedTunersResponder(
            [.. view.Devices.Select(DetectedDeviceResponder.Of)],
            view.Added,
            view.Missing,
            [.. view.Mismatched.Select(TunerKindMismatchResponder.Of)]);
    }
}
