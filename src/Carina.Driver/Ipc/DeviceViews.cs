using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

namespace Carina.Driver.Ipc;

public static class DeviceViews
{
    public static IReadOnlyList<DetectedDeviceDto> Detected(
        IReadOnlyList<TunerDetection> detections
    ) =>
        [
            .. detections.Select(detection => new DetectedDeviceDto
            {
                DeviceId = detection.DeviceId,
                Detection = detection.Detection,
                Kinds = [.. detection.Receives.Select(Wire)],
                Detail = detection.Detail,
            }),
        ];

    public static TunerKind Wire(DeviceKind kind) =>
        kind switch
        {
            DeviceKind.Terrestrial => TunerKind.Terrestrial,
            DeviceKind.Satellite => TunerKind.Satellite,
            _ => TunerKind.Unspecified,
        };
}
