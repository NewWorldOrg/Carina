using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// The bridge's I2C master, driven one byte at a time through memory requests.
/// </summary>
public sealed class BridgeI2c(BridgeMemory memory)
{
    public const uint Status = 0xFD8FE0;
    public const uint Control = 0xFD8FE4;
    public const uint Clock = 0xFD8FE8;
    public const uint Data = 0xFD8FF0;
    public const uint Divider = 0xFD8FF4;
    public const uint DividerValue = 0x08;

    public const uint SendAfterStart = 0x50;
    public const uint SendAfterRepeatedStart = 0x70;
    public const uint ReceiveAndAcknowledge = 0x58;
    public const uint Idle = 0x40;

    public const uint StatusBusy = 0x80;
    public const uint StatusNoAcknowledge = 0x10;
    public const uint ControlBusError = 0x80;
    public const uint ControlByteDone = 0x01;

    public const int MostPolls = 2000;

    private readonly Lock gate = new();

    public void Write(byte address, ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            try
            {
                Begin(address, bytes);
                End();
            }
            catch
            {
                Release();

                throw;
            }
        }
    }

    public void WriteThenRead(byte address, ReadOnlySpan<byte> bytes, Span<byte> into)
    {
        lock (gate)
        {
            try
            {
                Begin(address, bytes);
                Send((uint)(address | 1), SendAfterRepeatedStart, address, "its address for reading");

                for (int index = 0; index < into.Length; index++)
                {
                    bool last = index == into.Length - 1;
                    memory.Write(Control, last ? SendAfterStart : ReceiveAndAcknowledge);
                    WaitForTheByte(address);
                    into[index] = (byte)memory.Read(Data);
                }

                End();
            }
            catch
            {
                Release();

                throw;
            }
        }
    }

    private void Begin(byte address, ReadOnlySpan<byte> bytes)
    {
        WaitUntilIdle(address);
        Send(address, SendAfterStart, address, "its address");

        foreach (byte value in bytes)
        {
            Send(value, SendAfterStart, address, "a data byte");
        }
    }

    private void End()
    {
        memory.Write(Control, Idle);
        WaitUntilIdle(0);
    }

    private void Release() => memory.Write(Control, Idle);

    private void Send(uint value, uint control, byte address, string what)
    {
        memory.Write(Data, value);
        memory.Write(Control, control);
        WaitForTheByte(address);

        if ((memory.Read(Status) & StatusNoAcknowledge) is not 0)
        {
            throw Fault($"the device at 0x{address:X2} did not acknowledge {what}");
        }
    }

    private void WaitForTheByte(byte address)
    {
        for (int poll = 0; poll < MostPolls; poll++)
        {
            uint control = memory.Read(Control);

            if ((control & ControlBusError) is not 0)
            {
                throw Fault($"the bus reported an error while talking to 0x{address:X2}");
            }

            if ((control & ControlByteDone) is not 0)
            {
                return;
            }
        }

        throw Fault($"a byte to 0x{address:X2} did not finish within {MostPolls} polls");
    }

    private void WaitUntilIdle(byte address)
    {
        for (int poll = 0; poll < MostPolls; poll++)
        {
            if ((memory.Read(Status) & StatusBusy) is 0)
            {
                return;
            }
        }

        throw Fault($"the bus stayed busy for {MostPolls} polls around 0x{address:X2}");
    }

    private DvbDeviceException Fault(string what) =>
        new($"{memory.Path}: I2C: {what}.", TuningFailure.DeviceUnusable);
}

public interface IRegisterBus
{
    ushort Read(int register);

    void Write(int register, ushort value);
}

public static class RegisterBusExtensions
{
    public static void Modify(this IRegisterBus bus, int register, ushort clear, ushort set) =>
        bus.Write(register, (ushort)((bus.Read(register) & ~clear) | set));

    public static uint ReadLong(this IRegisterBus bus, int upper) =>
        ((uint)bus.Read(upper) << 16) | bus.Read(upper + 1);

    public static void WriteLong(this IRegisterBus bus, int upper, uint value)
    {
        bus.Write(upper, (ushort)(value >> 16));
        bus.Write(upper + 1, (ushort)value);
    }
}

/// <summary>
/// The demodulator's registers: a two byte register number and a 16 bit value, both upper byte first.
/// </summary>
public sealed class Dib8000Bus(BridgeI2c i2c) : IRegisterBus
{
    public const byte Address = 0x12;

    public ushort Read(int register)
    {
        Span<byte> value = stackalloc byte[2];
        i2c.WriteThenRead(Address, [(byte)(register >> 8), (byte)register], value);

        return (ushort)((value[0] << 8) | value[1]);
    }

    public void Write(int register, ushort value) =>
        i2c.Write(Address, [(byte)(register >> 8), (byte)register, (byte)(value >> 8), (byte)value]);
}

/// <summary>
/// The tuner's registers, behind the demodulator: a one byte register number and a 16 bit value.
/// </summary>
public sealed class Dib0090Bus(BridgeI2c i2c) : IRegisterBus
{
    public const byte Address = 0xC0;

    public ushort Read(int register)
    {
        Span<byte> value = stackalloc byte[2];
        i2c.WriteThenRead(Address, [(byte)register], value);

        return (ushort)((value[0] << 8) | value[1]);
    }

    public void Write(int register, ushort value) =>
        i2c.Write(Address, [(byte)register, (byte)(value >> 8), (byte)value]);
}
