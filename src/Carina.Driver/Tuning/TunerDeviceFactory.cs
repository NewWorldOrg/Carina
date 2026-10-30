using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tuning;

public interface ITunerDevice : IDisposable
{
    long Overflows { get; }

    ISignalQualitySource? Quality => null;

    byte[] Read(int count, CancellationToken cancellationToken);
}

public interface ITunerDeviceFactory
{
    ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune, HeadHold hold);
}

public interface ITunerDeviceCheck
{
    void Check(DeviceSettings device);
}

public sealed class TunerDeviceFactory : ITunerDeviceFactory, ITunerDeviceCheck
{
    private readonly TunerBackend backend;
    private readonly TimeProvider time;
    private readonly Lazy<IDvbSystemCalls> systemCalls;
    private readonly Lazy<IUsbHost> usbHost;
    private readonly DevicePacing pacing;
    private readonly IDescramblerFactory descramblers;
    private readonly ILogger? logger;
    private readonly DvbTunerSettings settings;

    public TunerDeviceFactory(
        DriverConfiguration configuration,
        TimeProvider time,
        IDescramblerFactory descramblers,
        ILogger<TunerDeviceFactory>? logger = null
    )
        : this(
            configuration,
            time,
            new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls()),
            new Lazy<IUsbHost>(() => new LinuxUsbHost()),
            DevicePacing.RealTime(time),
            descramblers,
            logger
        )
    { }

    public TunerDeviceFactory(DriverConfiguration configuration, TimeProvider time)
        : this(configuration, time, NoDescrambling.Instance) { }

    private TunerDeviceFactory(
        DriverConfiguration configuration,
        TimeProvider time,
        Lazy<IDvbSystemCalls> systemCalls,
        Lazy<IUsbHost> usbHost,
        DevicePacing pacing,
        IDescramblerFactory descramblers,
        ILogger? logger = null
    )
    {
        this.usbHost = usbHost;
        this.pacing = pacing;
        this.logger = logger;
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
            new Lazy<IUsbHost>(() => new LinuxUsbHost()),
            DevicePacing.RealTime(time),
            descramblers ?? NoDescrambling.Instance
        );

    public static TunerDeviceFactory Using(DriverConfiguration configuration, IUsbHost usbHost, DevicePacing pacing) =>
        new(
            configuration,
            TimeProvider.System,
            new Lazy<IDvbSystemCalls>(() => new LinuxDvbSystemCalls()),
            new Lazy<IUsbHost>(() => usbHost),
            pacing,
            NoDescrambling.Instance
        );

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune, HeadHold hold) =>
        backend switch
        {
            TunerBackend.Fake => Synthetic(tune?.ToLegacyRequest() ?? tuning),
            TunerBackend.Dvb => OpenDvb(device, tuning, tune, hold),
            TunerBackend.Usb => OpenUsb(device, tuning, tune),
            _ => throw BackendNeverEstablished(),
        };

    public void Check(DeviceSettings device)
    {
        switch (backend)
        {
            case TunerBackend.Fake:
                return;
            case TunerBackend.Dvb:
                DvbTunerDevice.OpenAndClose(systemCalls.Value, PathsOf(device));

                return;
            case TunerBackend.Usb:
                CechZd1TunerDevice.OpenAndClose(usbHost.Value, UsbPathOf(device));

                return;
            default:
                throw BackendNeverEstablished();
        }
    }

    private static InvalidOperationException BackendNeverEstablished() =>
        new(
            "The tuner backend was never established; the configuration should have been rejected."
        );

    private static DvbDevicePaths PathsOf(DeviceSettings device) =>
        DvbDevicePaths.TryDerive(device.DevicePath, out DvbDevicePaths? paths, out string? problem)
            ? paths
            : throw DvbFailure.Refused($"devices['{device.Id}']: {problem}");

    private static string UsbPathOf(DeviceSettings device) =>
        device.DevicePath ?? throw DvbFailure.Refused($"devices['{device.Id}']: a usb device needs a devicePath.");

    private ITunerDevice OpenUsb(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
    {
        if (DvbTuneRequest.Resolve(tune, tuning) is not TerrestrialChannel channel)
        {
            throw DvbFailure.Refused(
                $"devices['{device.Id}']: the usb tuner receives terrestrial broadcasting only, and this tune asks for a satellite."
            );
        }

        return CechZd1TunerDevice.Open(
            usbHost.Value,
            UsbPathOf(device),
            DvbFrequency.TerrestrialHertz(channel.PhysicalChannel),
            pacing,
            settings.BytePatience,
            logger
        );
    }

    private static ITunerDevice Synthetic(TuningRequest tuning) =>
        new FakeTunerDevice(tuning.PhysicalChannel, tuning.ServiceId);

    /// <summary>
    /// Tunes the device while the card is asked for, so that the card's answer to the initial setting is not added to
    /// the time the frontend takes to lock. A card that answers after the tuning has failed is let go of.
    /// </summary>
    private ITunerDevice OpenDvb(DeviceSettings device, TuningRequest tuning, TuneParams? tune, HeadHold hold)
    {
        DvbDevicePaths paths = PathsOf(device);
        Task<IDescrambler?>? asking = descramblers.Unscrambles ? Task.Run(() => descramblers.Open(hold)) : null;
        ITunerDevice opened;

        try
        {
            opened = DvbTunerDevice.Open(
                systemCalls.Value,
                time,
                paths,
                DvbTuneRequest.Resolve(tune, tuning),
                LnbPower.For(device.Kind, device.LnbPower),
                settings with { ReadsFromTheLock = HeadHolds.ReadsFromTheLock(hold) },
                CancellationToken.None
            );
        }
        catch
        {
            LetGoOnceAnswered(asking);

            throw;
        }

        if (asking is null)
        {
            return opened;
        }

        IDescrambler? descrambler;

        try
        {
            descrambler = asking.GetAwaiter().GetResult();
        }
        catch
        {
            opened.Dispose();

            throw;
        }

        return Unscrambling(opened, descrambler, hold);
    }

    private static void LetGoOnceAnswered(Task<IDescrambler?>? asking) =>
        asking?.ContinueWith(
            static answered =>
            {
                if (answered.IsCompletedSuccessfully)
                {
                    answered.Result?.Dispose();
                }
                else
                {
                    _ = answered.Exception;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

    private ITunerDevice Unscrambling(ITunerDevice opened, IDescrambler? descrambler, HeadHold hold)
    {
        if (descrambler is null)
        {
            logger?.LogWarning(
                "No card answered the reader as this tuner opened, so what it carries stays scrambled until one does; the card is asked for again while the tuner is read."
            );
        }

        try
        {
            return new DescramblingTunerDevice(opened, descrambler, descramblers, time, logger, hold);
        }
        catch
        {
            descrambler?.Dispose();

            throw;
        }
    }
}
