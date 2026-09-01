using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning;

public interface ITunerDevice : IDisposable
{
    long Overflows { get; }

    ISignalQualitySource? Quality => null;

    byte[] Read(int count, CancellationToken cancellationToken);

    byte[] WhatIsHeldBack() => [];
}

public interface ITunerDeviceFactory
{
    ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune);
}

public sealed class TunerDeviceFactory : ITunerDeviceFactory
{
    private readonly TunerBackend backend;
    private readonly TimeProvider time;
    private readonly Lazy<IDvbSystemCalls> systemCalls;
    private readonly IDescramblerFactory descramblers;
    private readonly DvbTunerSettings settings;

    public TunerDeviceFactory(
        DriverConfiguration configuration,
        TimeProvider time,
        IDescramblerFactory descramblers
    )
        : this(
            configuration,
            time,
            new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls()),
            descramblers
        ) { }

    public TunerDeviceFactory(DriverConfiguration configuration, TimeProvider time)
        : this(configuration, time, NoDescrambling.Instance) { }

    private TunerDeviceFactory(
        DriverConfiguration configuration,
        TimeProvider time,
        Lazy<IDvbSystemCalls> systemCalls,
        IDescramblerFactory descramblers
    )
    {
        backend = configuration.Tuner?.Backend ?? TunerBackend.Unspecified;
        this.time = time;
        this.systemCalls = systemCalls;
        this.descramblers = descramblers;
        settings = DvbTunerSettings.Default with
        {
            DemuxBufferBytes =
                configuration.Tuner?.DemuxBufferBytes ?? TunerSettings.DefaultDemuxBufferBytes,
        };
    }

    public static TunerDeviceFactory Using(
        DriverConfiguration configuration,
        TimeProvider time,
        IDvbSystemCalls systemCalls,
        IDescramblerFactory? descramblers = null
    ) =>
        new(
            configuration,
            time,
            new Lazy<IDvbSystemCalls>(() => systemCalls),
            descramblers ?? NoDescrambling.Instance
        );

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune) =>
        backend switch
        {
            TunerBackend.Fake => Synthetic(tune?.ToLegacyRequest() ?? tuning),
            TunerBackend.Dvb => OpenDvb(device, tuning, tune),
            _ => throw new InvalidOperationException(
                "The tuner backend was never established; the configuration should have been rejected."
            ),
        };

    private static ITunerDevice Synthetic(TuningRequest tuning) =>
        new FakeTunerDevice(tuning.PhysicalChannel, tuning.ServiceId);

    private ITunerDevice OpenDvb(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
    {
        if (!DvbDevicePaths.TryDerive(device.DevicePath, out DvbDevicePaths? paths, out string? problem))
        {
            throw DvbFailure.Refused($"devices['{device.Id}']: {problem}");
        }

        ITunerDevice opened = DvbTunerDevice.Open(
            systemCalls.Value,
            time,
            paths,
            DvbTuneRequest.Resolve(tune, tuning),
            LnbPower.For(device.Kind, device.LnbPower),
            settings,
            CancellationToken.None
        );

        return Unscrambling(opened);
    }

    private ITunerDevice Unscrambling(ITunerDevice opened)
    {
        IDescrambler? descrambler = descramblers.Open();
        if (descrambler is null)
        {
            return opened;
        }

        try
        {
            return new DescramblingTunerDevice(opened, descrambler);
        }
        catch
        {
            descrambler.Dispose();

            throw;
        }
    }
}
