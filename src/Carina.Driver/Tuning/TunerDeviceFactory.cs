using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning;

public interface ITunerDevice : IDisposable
{
    long Overflows { get; }

    byte[] Read(int count, CancellationToken cancellationToken);
}

public interface ITunerDeviceFactory
{
    ITunerDevice Create(DeviceSettings device, TuningRequest tuning);
}

public sealed class TunerDeviceFactory : ITunerDeviceFactory
{
    private readonly TunerBackend backend;
    private readonly TimeProvider time;
    private readonly Lazy<IDvbSystemCalls> systemCalls;
    private readonly DvbTunerSettings settings;

    public TunerDeviceFactory(DriverConfiguration configuration, TimeProvider time)
        : this(configuration, time, new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls())) { }

    private TunerDeviceFactory(
        DriverConfiguration configuration,
        TimeProvider time,
        Lazy<IDvbSystemCalls> systemCalls
    )
    {
        backend = configuration.Tuner?.Backend ?? TunerBackend.Unspecified;
        this.time = time;
        this.systemCalls = systemCalls;
        settings = DvbTunerSettings.Default;
    }

    public static TunerDeviceFactory Using(
        DriverConfiguration configuration,
        TimeProvider time,
        IDvbSystemCalls systemCalls
    ) => new(configuration, time, new Lazy<IDvbSystemCalls>(() => systemCalls));

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning) =>
        backend switch
        {
            TunerBackend.Fake => new FakeTunerDevice(tuning.PhysicalChannel, tuning.ServiceId),
            TunerBackend.Dvb => OpenDvb(device, tuning),
            _ => throw new InvalidOperationException(
                "The tuner backend was never established; the configuration should have been rejected."
            ),
        };

    private ITunerDevice OpenDvb(DeviceSettings device, TuningRequest tuning)
    {
        if (!DvbDevicePaths.TryDerive(device.DevicePath, out var paths, out var problem))
        {
            throw DvbFailure.Refused($"devices['{device.Id}']: {problem}");
        }

        return DvbTunerDevice.Open(
            systemCalls.Value,
            time,
            paths,
            DvbTuneRequest.Resolve(tuning),
            LnbPower.For(device.Kind, device.LnbPower),
            settings,
            CancellationToken.None
        );
    }
}
