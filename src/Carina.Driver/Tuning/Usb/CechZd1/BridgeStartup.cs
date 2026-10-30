using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// Brings the bridge from how it powers on to a running I2C master with the demodulator powered and out of reset.
/// </summary>
public static class BridgeStartup
{
    public const uint SetUpMark = 0x82008;

    public const uint GpioValue = 0x90018;

    public const uint GpioDirection = 0x90014;

    public const ushort DemodulatorReset = 0x0001;

    public const ushort DemodulatorPower = 0x0040;

    public static readonly IReadOnlyList<uint> ClockedBlocks =
    [
        0x14D0000,
        0x10D0000,
        0x12D0000,
        0x13D0000,
        0xE70000,
        0xC70000,
        0xD00000,
        0x1600000,
        0x1700000,
    ];

    private const uint InterruptLevels = 0x440;
    private const int InterruptLevelWords = 16;
    private const uint NoInterrupt = 0x1F1F1F1F;

    public static void RequirePowerOnState(BridgeMemory memory)
    {
        ushort mark = memory.ReadHalf(SetUpMark);

        if (mark is not 0)
        {
            throw new DvbDeviceException(
                $"{memory.Path}: something other than this driver has set the bridge up since it was powered on (0x{SetUpMark:X} reads 0x{mark:X4}), and this driver only starts it from power on. Unplug the tuner and plug it back in.",
                TuningFailure.DeviceUnusable
            );
        }
    }

    public static void Start(BridgeMemory memory, DevicePacing pacing)
    {
        RequirePowerOnState(memory);
        AddressWidening.Widen(memory);
        RequirePowerOnState(memory);
        StartClocks(memory);
        MaskInterrupts(memory);
        StartI2cMaster(memory);
        PowerTheDemodulator(memory, pacing);
    }

    public static void StartClocks(BridgeMemory memory)
    {
        memory.WriteHalf(0xF1010C, 0x8201);
        memory.WriteHalf(0xF10114, 0x8008);
        memory.WriteHalf(0xF10118, 0xFFFF);

        foreach (uint block in ClockedBlocks)
        {
            memory.Modify(block + 0x404, 0, 0x00040000);
        }

        memory.WriteHalf(0xF1011C, 0x0006);
        memory.WriteHalf(0xF10128, 0x6000);
        memory.WriteHalf(0xF1012C, 0x4007);
        memory.WriteHalf(0xF12144, 0x0003);

        foreach (uint register in (uint[])[0xF12178, 0xF1217C, 0xF12198, 0xF1219C])
        {
            memory.WriteHalf(register, 0x0880);
        }
    }

    public static void MaskInterrupts(BridgeMemory memory)
    {
        uint[] levels = memory.ReadWords(InterruptLevels, InterruptLevelWords);

        for (int word = 0; word < levels.Length; word++)
        {
            uint masked = levels[word] | NoInterrupt;

            if (masked != levels[word])
            {
                memory.Write(InterruptLevels + (uint)(4 * word), masked);
            }
        }
    }

    public static void StartI2cMaster(BridgeMemory memory)
    {
        memory.Modify(0xFD8010, 0, 0x10);
        memory.Modify(0xFD8014, 0x10, 0);
        memory.Write(0xFD80C0, 1);
        memory.Write(0xFD80C0, 0);
        memory.Write(0xFD80C4, 0);
        memory.WriteHalf(0x90114, 0x8008);
        memory.Write(0xFD8FF8, 0x06);
        memory.Write(BridgeI2c.Divider, BridgeI2c.DividerValue);
        memory.Write(BridgeI2c.Clock, 0x3C);
        memory.Write(BridgeI2c.Control, BridgeI2c.Idle);
    }

    public static void PowerTheDemodulator(BridgeMemory memory, DevicePacing pacing)
    {
        ushort value = memory.ReadHalf(GpioValue);
        ushort direction = memory.ReadHalf(GpioDirection);
        const ushort Both = DemodulatorReset | DemodulatorPower;

        memory.WriteHalf(GpioValue, (ushort)((value & ~DemodulatorReset) | DemodulatorPower));
        memory.WriteHalf(GpioDirection, (ushort)(direction | Both));
        pacing.SleepMilliseconds(50);
        memory.WriteHalf(GpioValue, (ushort)(value | Both));
        pacing.SleepMilliseconds(10);
    }
}
