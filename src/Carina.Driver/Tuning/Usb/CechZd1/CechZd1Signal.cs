using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

public sealed record SignalRegisters(ushort StatusBefore, ushort Noise, ushort Signal, ushort Strength, ushort StatusAfter);

/// <summary>
/// Reads the demodulator's status, signal strength and carrier to noise ratio off its registers.
/// </summary>
public static class CechZd1Signal
{
    public const ushort Synchronised = 0x1000;

    public const ushort LayerData = 0x00E0;

    private const ushort TmccSynchronised = 0x001E;

    private static readonly IReadOnlyList<(int Level, int Value)> StrengthCurve =
    [
        (55953, 108500), (55394, 108000), (53834, 107000), (52863, 106000), (52239, 105000), (52012, 104000),
        (51803, 103000), (51566, 102000), (51356, 101000), (51112, 100000), (50869, 99000), (50600, 98000),
        (50363, 97000), (50117, 96000), (49889, 95000), (49680, 94000), (49493, 93000), (49302, 92000),
        (48929, 91000), (48416, 90000), (48035, 89000), (47593, 88000), (47282, 87000), (46953, 86000),
        (46698, 85000), (45617, 84000), (44773, 83000), (43845, 82000), (43020, 81000), (42010, 80000),
        (0, 0),
    ];

    public static bool LockedWithData(ushort status) =>
        (status & Synchronised) is not 0 && (status & LayerData) is not 0;

    public static FrontendStatus StatusOf(ushort status)
    {
        FrontendStatus flags = FrontendStatus.None;

        if ((status & TmccSynchronised) is not 0)
        {
            flags |= FrontendStatus.Signal | FrontendStatus.Carrier;
        }

        if ((status & Synchronised) is not 0)
        {
            flags |= FrontendStatus.Signal | FrontendStatus.Carrier | FrontendStatus.Sync;
        }

        if ((status & LayerData) is not 0)
        {
            flags |= FrontendStatus.Viterbi;
        }

        return LockedWithData(status) ? flags | FrontendStatus.Lock : flags;
    }

    public static double StrengthDbm(ushort register)
    {
        int level = 65535 - register;

        if (level >= StrengthCurve[0].Level)
        {
            return ToDbm(StrengthCurve[0].Value);
        }

        for (int row = 1; row < StrengthCurve.Count; row++)
        {
            (int floor, int value) = StrengthCurve[row];

            if (level >= floor)
            {
                (int ceiling, int above) = StrengthCurve[row - 1];

                return ToDbm(value + ((long)(level - floor) * (above - value) / (ceiling - floor)));
            }
        }

        return ToDbm(0);
    }

    public static uint Expand(ushort raw)
    {
        uint mantissa = (uint)(raw >> 6) & 0xFF;
        int exponent = raw & 0x3F;
        exponent = exponent >= 32 ? exponent - 64 : exponent;

        return mantissa << ((exponent + 16) & 0x1F);
    }

    public static CarrierToNoise CarrierToNoiseFrom(SignalRegisters registers)
    {
        LockWindow locked = new(StatusOf(registers.StatusBefore), StatusOf(registers.StatusAfter));

        if (locked.HeldAtNeitherEnd)
        {
            return CarrierToNoise.WithoutLock;
        }

        if (locked.Wavered)
        {
            return CarrierToNoise.Unavailable;
        }

        return Decibels(Expand(registers.Signal), Expand(registers.Noise)) is { } decibels
            ? CarrierToNoise.Measured(decibels)
            : CarrierToNoise.Unavailable;
    }

    public static double? Decibels(uint signal, uint noise)
    {
        if (noise is 0)
        {
            return null;
        }

        uint whole = unchecked((signal / noise) << 16);
        uint ratio = unchecked(whole + (((signal << 16) - (noise * whole)) / noise));
        uint integer = ratio >> 16;

        return integer is 0 ? 0 : 10 * Math.Log10(integer);
    }

    private static double ToDbm(long value) => (value - 131000) / 1000.0;
}
