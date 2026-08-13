using Carina.Contracts;
using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning;

public interface ITunerDevice : IDisposable
{
    byte[] Read(int count);
}

public interface ITunerDeviceFactory
{
    ITunerDevice Create(DeviceSettings device, TuningRequest tuning);
}

public sealed class TunerDeviceFactory(DriverConfiguration configuration) : ITunerDeviceFactory
{
    private readonly TunerBackend backend =
        configuration.Tuner?.Backend ?? TunerBackend.Unspecified;

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning) =>
        backend switch
        {
            TunerBackend.Fake => new FakeTunerDevice(
                tuning.PhysicalChannel,
                tuning.ServiceId
            ),
            TunerBackend.Dvb => throw new NotSupportedException(
                $"The dvb backend is not implemented yet, so '{device.Id}' cannot be opened."
            ),
            _ => throw new InvalidOperationException(
                "The tuner backend was never established; the configuration should have been rejected."
            ),
        };
}
