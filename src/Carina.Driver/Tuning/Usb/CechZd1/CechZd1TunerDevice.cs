using Carina.Driver.Transport;
using Carina.Driver.Tuning.Dvb;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// The CECH-ZD1 terrestrial tuner on USB, driven from its bridge's power-on state with nothing written to flash.
/// </summary>
public sealed class CechZd1TunerDevice : ITunerDevice, ISignalQualitySource
{
    public static readonly UsbModel Model = new(0x054C, 0x04B2);

    public const byte Interface = 0;

    public static readonly TimeSpan TransferPatience = TimeSpan.FromMilliseconds(300);

    private readonly IUsbConnection usb;
    private readonly CechZd1Frontend frontend;
    private readonly TransportCapture capture;
    private readonly IUsbBulkReader reader;
    private readonly DevicePacing pacing;
    private readonly TimeSpan bytePatience;
    private readonly CaptureUnpacker unpacker = new();
    private readonly List<byte> packets = [];
    private readonly byte[] transfer = new byte[TransportCapture.TransferBytes];
    private readonly Lock gate = new();
    private bool closed;

    private CechZd1TunerDevice(
        IUsbConnection usb,
        CechZd1Frontend frontend,
        TransportCapture capture,
        IUsbBulkReader reader,
        DevicePacing pacing,
        TimeSpan bytePatience
    )
    {
        this.usb = usb;
        this.frontend = frontend;
        this.capture = capture;
        this.reader = reader;
        this.pacing = pacing;
        this.bytePatience = bytePatience;
    }

    public long Overflows => unpacker.Realignments;

    public ISignalQualitySource? Quality => this;

    public static void Recognise(IUsbHost host, string path)
    {
        UsbModel model = host.ModelAt(path);

        if (model != Model)
        {
            throw DvbFailure.Refused(
                $"{path}: the device there is {model}, not the {Model} tuner this backend drives."
            );
        }
    }

    public static CechZd1TunerDevice Open(
        IUsbHost host,
        string path,
        long hertz,
        DevicePacing pacing,
        TimeSpan bytePatience,
        ILogger? logger = null
    )
    {
        Recognise(host, path);
        IUsbConnection usb = host.Open(path, Interface);
        CechZd1Frontend? frontend = null;

        try
        {
            var memory = new BridgeMemory(usb);
            BridgeStartup.Start(memory, pacing);
            var i2c = new BridgeI2c(memory);
            frontend = new CechZd1Frontend(new Dib8000Bus(i2c), new Dib0090Bus(i2c), pacing, path);
            frontend.Initialise();
            var capture = new TransportCapture(memory);
            capture.Prepare();

            if (frontend.Tune(hertz) is SearchOutcome.Failed)
            {
                throw DvbFailure.NoLock(
                    $"{path}: the demodulator did not synchronise on {hertz / 1000} kHz. Nothing was received, so no bytes will follow."
                );
            }

            IUsbBulkReader reader = usb.ReadBulk(
                TransportCapture.Endpoint,
                TransportCapture.TransferBytes,
                TransportCapture.TransfersInFlight
            );
            capture.Start();
            var device = new CechZd1TunerDevice(usb, frontend, capture, reader, pacing, bytePatience);
            device.Report(logger, hertz);

            return device;
        }
        catch
        {
            Quietly(() => frontend?.Sleep());
            Quietly(() => frontend?.Demodulator.LedGreen());
            usb.Dispose();

            throw;
        }
    }

    public static void OpenAndClose(IUsbHost host, string path)
    {
        Recognise(host, path);
        host.Open(path, Interface).Dispose();
    }

    public SignalQuality Measure()
    {
        SignalRegisters registers;

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            registers = frontend.ReadSignal();
        }

        return new SignalQuality(
            new LockWindow(CechZd1Signal.StatusOf(registers.StatusBefore), CechZd1Signal.StatusOf(registers.StatusAfter)),
            CechZd1Signal.CarrierToNoiseFrom(registers),
            new PostViterbiErrors(SignalReading.NotImplementedByThisTuner, [])
        );
    }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = pacing.Now + bytePatience;

        while (packets.Count < TsPacketReader.PacketLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int arrived = reader.Take(transfer, TransferPatience);

            if (arrived > 0)
            {
                unpacker.Unpack(transfer.AsSpan(0, arrived), packets);
            }
            else if (pacing.Now >= deadline)
            {
                throw NothingArrived();
            }
        }

        int whole = Math.Max(count / TsPacketReader.PacketLength, 1) * TsPacketReader.PacketLength;
        int taking = Math.Min(packets.Count / TsPacketReader.PacketLength * TsPacketReader.PacketLength, whole);
        byte[] delivered = new byte[taking];
        packets.CopyTo(0, delivered, 0, taking);
        packets.RemoveRange(0, taking);

        return delivered;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            Quietly(capture.Stop);
            Quietly(reader.Dispose);
            Quietly(() => frontend.Demodulator.LedGreen());
            Quietly(frontend.Sleep);
            usb.Dispose();
        }
    }

    private DvbDeviceException NothingArrived()
    {
        string waited = $"{bytePatience.TotalSeconds:0.#} seconds";
        SignalRegisters registers;

        try
        {
            lock (gate)
            {
                registers = frontend.ReadSignal();
            }
        }
        catch (DvbDeviceException)
        {
            return DvbFailure.Refused(
                $"{usb.Path}: no transport stream arrived within {waited}, and the demodulator would not say whether it is still locked."
            );
        }

        return CechZd1Signal.LockedWithData(registers.StatusAfter)
            ? DvbFailure.LockedWithoutData(
                $"{usb.Path}: the demodulator is locked and no transport stream arrived within {waited}."
            )
            : DvbFailure.NoLock(
                $"{usb.Path}: no transport stream arrived within {waited}, and the demodulator is no longer locked (0x{registers.StatusAfter:X4})."
            );
    }

    private void Report(ILogger? logger, long hertz)
    {
        if (logger is null)
        {
            return;
        }

        SignalRegisters registers = frontend.ReadSignal();
        double? decibels = CechZd1Signal.Decibels(
            CechZd1Signal.Expand(registers.Signal),
            CechZd1Signal.Expand(registers.Noise)
        );
        logger.LogInformation(
            "{Path} locked on {Kilohertz} kHz: status 0x{Status:X4}, signal {Strength:0.0} dBm, C/N {CarrierToNoise:0.0} dB.",
            usb.Path,
            hertz / 1000,
            registers.StatusAfter,
            CechZd1Signal.StrengthDbm(registers.Strength),
            decibels
        );
    }

    private static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch (DvbDeviceException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
