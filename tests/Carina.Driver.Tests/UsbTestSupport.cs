using System.Buffers.Binary;

using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tests;

/// <summary>
/// A register file standing in for one I2C device, which answers reads with what was last written unless a reading
/// is scripted for that register.
/// </summary>
public sealed class FakeChip(int registerBytes) : IRegisterBus
{
    public int RegisterBytes { get; } = registerBytes;

    public Dictionary<int, ushort> Registers { get; } = [];

    public Dictionary<int, Func<ushort>> Answers { get; } = [];

    public List<(int Register, ushort Value)> Written { get; } = [];

    public List<int> ReadFrom { get; } = [];

    public ushort Read(int register)
    {
        ReadFrom.Add(register);

        return Answers.TryGetValue(register, out Func<ushort>? answer)
            ? answer()
            : Registers.GetValueOrDefault(register);
    }

    public void Write(int register, ushort value)
    {
        Written.Add((register, value));
        Registers[register] = value;
    }

    public void Answer(int register, params ushort[] values)
    {
        var queue = new Queue<ushort>(values);
        ushort last = values[^1];
        Answers[register] = () => queue.Count > 0 ? queue.Dequeue() : last;
    }

    public IReadOnlyList<ushort> WritesTo(int register) =>
        [.. Written.Where(write => write.Register == register).Select(write => write.Value)];

    public ushort Last(int register) => WritesTo(register)[^1];
}

/// <summary>
/// The bridge as a memory, with its power-on limits on addresses and multi-word writes, an I2C master that talks to
/// fake chips, and a bulk endpoint that hands out queued transfers.
/// </summary>
public sealed class FakeBridge : IUsbConnection
{
    private const uint Status = BridgeI2c.Status;
    private const uint Control = BridgeI2c.Control;
    private const uint Data = BridgeI2c.Data;
    private const uint EndpointReset = 0xFDBD08;

    private readonly List<byte> sent = [];
    private readonly Queue<byte> answering = new();
    private FakeChip? talkingTo;
    private I2cState state;

    public FakeBridge()
    {
        foreach ((uint address, uint word) in AddressWidening.Patched)
        {
            Memory[address] = word;
        }
    }

    private enum I2cState
    {
        Idle,
        Writing,
        Reading,
    }

    public string Path => "/dev/bus/usb/001/002";

    public Dictionary<uint, uint> Memory { get; } = [];

    public List<(uint Address, uint[] Words)> Writes { get; } = [];

    public List<UsbSetup> Requests { get; } = [];

    public Dictionary<byte, FakeChip> Chips { get; } = [];

    public Queue<byte[]> Transfers { get; } = new();

    public bool Disposed { get; private set; }

    public bool BulkClosed { get; private set; }

    public (byte Endpoint, int Bytes, int InFlight)? BulkOpened { get; private set; }

    public bool NotAcknowledging { get; set; }

    public Action<TimeSpan>? Idle { get; set; }

    public bool PowerOnRules =>
        AddressWidening.Patched.All(patch => Memory.GetValueOrDefault(patch.Address) == patch.AtPowerOn);

    public static uint AddressOf(UsbSetup setup) =>
        ((uint)(setup.Value & 0xFF) << 24) | ((uint)(setup.Value >> 8) << 16) | setup.Index;

    public uint Word(uint address) => Memory.GetValueOrDefault(Reached(address));

    public IReadOnlyList<uint> WritesAt(uint address) =>
        [.. Writes.Where(write => write.Address == Reached(address)).Select(write => write.Words[0])];

    public int ControlIn(UsbSetup setup, Span<byte> data, TimeSpan timeout)
    {
        Requests.Add(setup);
        uint address = Reached(AddressOf(setup));

        for (int word = 0; word < data.Length / 4; word++)
        {
            uint at = address + (uint)(4 * word);
            BinaryPrimitives.WriteUInt32BigEndian(data[(4 * word)..], Memory.GetValueOrDefault(at));
        }

        return data.Length;
    }

    public void ControlOut(UsbSetup setup, ReadOnlySpan<byte> data, TimeSpan timeout)
    {
        Requests.Add(setup);
        uint address = Reached(AddressOf(setup));
        int words = PowerOnRules ? 1 : data.Length / 4;
        uint[] written = new uint[words];

        for (int word = 0; word < words; word++)
        {
            written[word] = BinaryPrimitives.ReadUInt32BigEndian(data[(4 * word)..]);
            Memory[address + (uint)(4 * word)] = written[word];
        }

        Writes.Add((address, written));

        if (address == EndpointReset)
        {
            Memory[address] = written[0] & ~1u;
        }

        if (address == Control)
        {
            Drive(written[0]);
        }
    }

    public IUsbBulkReader ReadBulk(byte endpoint, int transferBytes, int transfersInFlight)
    {
        BulkOpened = (endpoint, transferBytes, transfersInFlight);

        return new Reader(this);
    }

    public void Dispose() => Disposed = true;

    private uint Reached(uint address)
    {
        uint reached = PowerOnRules ? address & 0xFFFFFF : address;

        return reached is >= 0x80000 and < 0xA0000 ? 0xF00000 + (reached & 0x3FFFF) : reached;
    }

    private void Drive(uint control)
    {
        bool acknowledged = true;

        switch (control)
        {
            case BridgeI2c.Idle:
                Commit();
                break;
            case BridgeI2c.SendAfterRepeatedStart:
                acknowledged = Address((byte)Memory.GetValueOrDefault(Data));
                break;
            case BridgeI2c.SendAfterStart when state is I2cState.Idle:
                acknowledged = Address((byte)Memory.GetValueOrDefault(Data));
                break;
            case BridgeI2c.SendAfterStart when state is I2cState.Writing:
                sent.Add((byte)Memory.GetValueOrDefault(Data));
                break;
            default:
                Memory[Data] = answering.Count > 0 ? answering.Dequeue() : 0u;
                break;
        }

        Memory[Control] = control | BridgeI2c.ControlByteDone;
        Memory[Status] = acknowledged && !NotAcknowledging ? 0 : BridgeI2c.StatusNoAcknowledge;
    }

    private bool Address(byte address)
    {
        Chips.TryGetValue((byte)(address & 0xFE), out talkingTo);

        if ((address & 1) is 0)
        {
            state = I2cState.Writing;
            sent.Clear();

            return talkingTo is not null;
        }

        state = I2cState.Reading;

        if (talkingTo is not null)
        {
            ushort value = talkingTo.Read(RegisterIn(talkingTo));
            answering.Enqueue((byte)(value >> 8));
            answering.Enqueue((byte)value);
        }

        return talkingTo is not null;
    }

    private void Commit()
    {
        if (state is I2cState.Writing && talkingTo is not null && sent.Count == talkingTo.RegisterBytes + 2)
        {
            int at = talkingTo.RegisterBytes;
            talkingTo.Write(RegisterIn(talkingTo), (ushort)((sent[at] << 8) | sent[at + 1]));
        }

        state = I2cState.Idle;
        answering.Clear();
    }

    private int RegisterIn(FakeChip chip) => chip.RegisterBytes is 2 ? (sent[0] << 8) | sent[1] : sent[0];

    private sealed class Reader(FakeBridge bridge) : IUsbBulkReader
    {
        public int Take(Span<byte> into, TimeSpan patience)
        {
            if (!bridge.Transfers.TryDequeue(out byte[]? transfer))
            {
                bridge.Idle?.Invoke(patience);

                return 0;
            }

            transfer.CopyTo(into);

            return transfer.Length;
        }

        public void Dispose() => bridge.BulkClosed = true;
    }
}

public sealed class FakeUsbHost(FakeBridge bridge) : IUsbHost
{
    public List<UsbDeviceNode> Nodes { get; } = [];

    public UsbModel Model { get; set; } = CechZd1TunerDevice.Model;

    public int Opened => OpenedPaths.Count;

    public List<string> OpenedPaths { get; } = [];

    public IReadOnlyList<UsbDeviceNode> List() => Nodes;

    public UsbModel ModelAt(string path) =>
        Nodes.FirstOrDefault(node => node.Path == path)?.Model ?? Model;

    public IUsbConnection Open(string path, byte interfaceNumber)
    {
        OpenedPaths.Add(path);

        return bridge;
    }
}

public static class UsbTestSupport
{
    public static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    public static (DevicePacing Pacing, ManualTimeProvider Clock, List<TimeSpan> Slept) Pacing()
    {
        var clock = new ManualTimeProvider(Start);
        var slept = new List<TimeSpan>();

        return (
            new DevicePacing(
                clock,
                span =>
                {
                    slept.Add(span);
                    clock.Advance(span);
                }
            ),
            clock,
            slept
        );
    }

    /// <summary>
    /// A demodulator and tuner that identify as the ones this driver knows and lock on whatever they are tuned to.
    /// </summary>
    public static (FakeChip Demodulator, FakeChip Tuner) LockingChips()
    {
        var demodulator = new FakeChip(2);
        demodulator.Answer(896, Dib8000.ChipId);
        demodulator.Answer(897, Dib8000.LatestVersion);
        demodulator.Answer(1284, 0x0002);
        demodulator.Answer(Dib8000.Status, 0x14FE);
        demodulator.Answer(570, 0x0072);
        demodulator.Answer(505, 1);
        demodulator.Answer(493, 3);
        demodulator.Answer(494, 10);
        demodulator.Answer(495, 0);
        demodulator.Answer(487, 2);
        demodulator.Answer(488, 3);
        demodulator.Answer(499, 1);
        demodulator.Answer(500, 1);
        demodulator.Answer(481, 1);
        demodulator.Answer(482, 5);
        demodulator.Answer(542, 100 << 6);
        demodulator.Answer(543, (100 << 6) | 6);
        demodulator.Answer(390, 65535 - 50000);

        var tuner = new FakeChip(1);
        tuner.Answer(0x1A, 0x0223);
        tuner.Answer(0x26, 0xFFFF);
        tuner.Answer(0x28, 0xFFFF);
        tuner.Answer(0x1D, 300);

        return (demodulator, tuner);
    }

    public static FakeBridge BridgeWith(FakeChip demodulator, FakeChip tuner)
    {
        var bridge = new FakeBridge();
        bridge.Chips[Dib8000Bus.Address] = demodulator;
        bridge.Chips[Dib0090Bus.Address] = tuner;

        return bridge;
    }

    public static byte[] Units(int count, int skip = 0, byte firstCounter = 0)
    {
        byte[] stream = new byte[(count * CaptureUnpacker.UnitBytes) - skip];
        byte[] whole = new byte[count * CaptureUnpacker.UnitBytes];

        for (int unit = 0; unit < count; unit++)
        {
            int at = unit * CaptureUnpacker.UnitBytes;
            whole[at + CaptureUnpacker.StampBytes] = 0x47;
            whole[at + CaptureUnpacker.StampBytes + 3] = (byte)(firstCounter + unit);
        }

        whole.AsSpan(skip).CopyTo(stream);

        return stream;
    }

    public static DvbDeviceException Refusal(Action action) => Assert.Throws<DvbDeviceException>(action);
}
