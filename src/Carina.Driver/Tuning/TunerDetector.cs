using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning;

public sealed record TunerDetection(
    string DeviceId,
    IReadOnlyList<DeviceKind> Receives,
    DeviceDetection Detection,
    string? Detail,
    string? DevicePath = null
);

public interface ITunerDetector
{
    IReadOnlyList<TunerDetection> Detect();
}

public sealed class FakeTunerDetector(DriverConfiguration configuration) : ITunerDetector
{
    public IReadOnlyList<TunerDetection> Detect() =>
    [
        .. (configuration.Devices ?? [])
            .Where(device => device?.Id is not null)
            .Select(Describe),
    ];

    private static TunerDetection Describe(DeviceSettings device)
    {
        IReadOnlyList<DeviceKind> receives =
            device.Kind is DeviceKind.Unspecified ? [] : [device.Kind];

        return new TunerDetection(
            device.Id!,
            receives,
            receives.Count is 0 ? DeviceDetection.Unreadable : DeviceDetection.Detected,
            receives.Count is 0
                ? "The configuration does not say what this synthetic tuner receives."
                : null,
            device.DevicePath
        );
    }
}

public static class TunerDetectors
{
    public static ITunerDetector For(DriverConfiguration configuration) =>
        (configuration.Tuner?.Backend ?? TunerBackend.Unspecified) switch
        {
            TunerBackend.Fake => new FakeTunerDetector(configuration),
            TunerBackend.Dvb => new DvbTunerDetector(),
            _ => throw new InvalidOperationException(
                "The tuner backend was never established; the configuration should have been rejected."
            ),
        };
}
