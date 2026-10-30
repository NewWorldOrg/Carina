using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

public enum Diversity
{
    InternalOnly = 0,

    Both = 1,
}

/// <summary>
/// The ISDB-T demodulator: its power, converter, output, pins and the settings a channel search writes.
/// </summary>
public sealed class Dib8000(IRegisterBus d, DevicePacing pacing)
{
    public const ushort ChipId = 0x01B3;

    public const ushort LatestVersion = 0x8002;

    public const int Status = 568;

    public const int ClockHertz = 54_000;

    public const int AgcReferenceBeforeAnyTune = 3530;

    public static readonly IReadOnlyList<ushort> KnownVersions = [0x8000, 0x8001, 0x8002];

    public static readonly IReadOnlyList<int> SegmentOrder = [6, 5, 7, 4, 8, 3, 9, 2, 10, 1, 11, 0, 12];

    public static readonly IReadOnlyList<ushort> ControlLoopDefaults =
        [7250, 7238, 7264, 7309, 7338, 7382, 7427, 7456, 7500, 7544];

    public static readonly IReadOnlyList<short> PartialReceptionCoefficients =
    [
        81, 80, 78, 74, 68, 61, 54, 45, 37, 28, 19, 11, 4, 1022, 1017, 1013, 1010, 1008, 1008, 1008, 1008, 1010,
        1014, 1017,
    ];

    public static readonly IReadOnlyList<short> FullBandCoefficients =
    [
        249, 226, 164, 82, 5, 981, 970, 988, 1018, 20, 31, 26, 8, 1012, 1000, 1018, 1012, 8, 15, 14, 9, 3, 1017, 1003,
    ];

    public static readonly IReadOnlyList<short> ThirteenSegmentCoefficients =
    [
        396, 305, 105, -51, -77, -12, 41, 31, -11, -30, -11, 14, 15, -2, -13, -7, 5, 8, 1, -6, -7, -3, 0, 1,
    ];

    private const ushort AllSegments = 0b1_1111_1111_1111;
    private const int ControlLoopFirst = 80;
    private const int FourBits = 0b1111;
    private const int CoefficientsFirst = 117;
    private const int DdsReachKilohertz = 27_000;
    private const uint DdsUnit = 67_108_864 / ClockHertz;

    private bool? agcWrittenForCBand;

    public ushort Version { get; private set; }

    public ushort DifferentialMask { get; private set; }

    public bool Differential { get; private set; }

    public void Identify(string path)
    {
        d.Write(1287, 0x0003);
        d.Read(896);
        ushort id = d.Read(896);

        if (id != ChipId)
        {
            throw Missing(path, $"the demodulator answers 0x{id:X4} where 0x{ChipId:X4} is expected");
        }

        ushort version = d.Read(897);

        if (!KnownVersions.Contains(version))
        {
            throw Missing(path, $"the demodulator reports version 0x{version:X4}, which this driver does not drive");
        }

        Version = version;
    }

    public void PowerAll() => Power(all: true);

    public void PowerInterfaceOnly() => Power(all: false);

    public void AdcOff()
    {
        d.Write(907, 0x3000);
        d.Write(908, 0x007A);
    }

    public void AdcSlowOn()
    {
        ushort a = d.Read(907);
        ushort b = (ushort)(d.Read(908) | 0x0003);
        d.Write(908, b);
        b = (ushort)(b & ~0x0002);
        d.Write(907, a);
        d.Write(908, b);
    }

    public void AdcSlowOff()
    {
        ushort a = d.Read(907);
        ushort b = (ushort)(d.Read(908) | 0x0003);
        d.Write(907, a);
        d.Write(908, b);
    }

    public void AdcOn()
    {
        ushort a = d.Read(907);
        ushort b = d.Read(908);
        d.Write(907, (ushort)(a & 0x0FFF));
        d.Write(908, (ushort)(b & 0x0003));
    }

    public void TransportOutput(bool on)
    {
        d.Write(299, (ushort)((d.Read(299) & 0x0050) | 0x0022));
        d.Write(300, 1792);
        d.Write(1286, (ushort)(on ? 0x0400 : 0));
        d.Write(1291, 0x0205);
    }

    public void Gpio(int pin, bool high)
    {
        ushort bit = (ushort)(1 << pin);
        d.Modify(1029, bit, 0);
        d.Modify(1030, high ? (ushort)0 : bit, high ? bit : (ushort)0);
    }

    public void LedRed() => Gpio(1, high: true);

    public void LedGreen() => d.Modify(1029, 0, 0x0002);

    public void WriteTimingFrequency(uint value) => d.WriteLong(29, value);

    public void WriteAgc(bool cBand, int reference)
    {
        if (agcWrittenForCBand == cBand)
        {
            return;
        }

        d.Write(76, 0x286A);
        d.Write(77, 787);
        d.Write(78, 10);
        d.Write(101, 118);
        d.Write(102, (28 * 32) + 26);
        d.Write(103, (31 * 64) + 51);
        d.Write(106, (ushort)reference);
        d.Write(107, 5 * 512);
        d.Write(108, (ushort)(cBand ? 0 : 65535));
        d.Write(109, 0);
        d.Write(110, 65535);
        d.Write(111, 0);
        d.Write(112, 32);
        d.Write(113, (143 * 256) + 144);
        d.Write(114, (114 * 256) + 227);
        d.Write(115, (116 * 256) + 117);
        d.Write(75, 114);
        d.Modify(923, 0x001C, 0x0004);
        agcWrittenForCBand = cBand;
    }

    public void WriteDdsOffset(int tunerKilohertz, int askedKilohertz)
    {
        int offset = tunerKilohertz - askedKilohertz;

        if (Math.Abs(offset) > DdsReachKilohertz)
        {
            return;
        }

        uint dds = unchecked((uint)Math.Abs(offset) * (offset < 0 ? (uint)-(int)DdsUnit : DdsUnit));
        d.Write(26, 0);
        d.Write(27, (ushort)((dds >> 16) & 0x1FF));
        d.Write(28, (ushort)dds);
    }

    public void WriteSyncWait(ChannelParameters channel)
    {
        int unit = channel.Fft switch
        {
            FftSize.EightK => 256,
            FftSize.FourK => 128,
            _ => 64,
        };
        int wait = (unit * (1 << (channel.Guard & 3)) * 3 / 2) + 48;
        d.Write(273, (ushort)((d.Read(273) & 0x000F) | (wait * 16)));
    }

    public void SetDiversity(Diversity diversity)
    {
        ushort switches = (ushort)(d.Read(273) & ~0x000F);
        d.Write(272, (ushort)(Differential ? 0 : 0x0200));
        d.Write(273, (ushort)(Differential ? switches : switches + 6));
        d.Write(270, (ushort)(diversity is Diversity.Both ? 6 : 1));
        d.Write(271, (ushort)(diversity is Diversity.Both ? 6 : 0));

        if (Version is LatestVersion)
        {
            ushort power = d.Read(903);
            d.Write(903, (ushort)(power & ~0x0008));
            pacing.SleepMilliseconds(30);
            d.Write(903, (ushort)(power | 0x0008));
        }
    }

    public void WriteLoopValues(int mode, bool afterLock)
    {
        d.Write(32, (ushort)(((afterLock ? 11 : 9) - mode) * 0x1000 + 0x0680));
        d.Write(37, (ushort)(afterLock ? ((5 + mode) * 32) + (10 - mode) : (3 * 32) + (8 - mode)));
    }

    public void WriteChannel(ChannelParameters channel, int sequence, bool searching)
    {
        int partial = searching ? 1 : channel.PartialReception;
        d.Write(10, (ushort)(sequence * 16));
        d.Modify(1, 0x0003, (ushort)(channel.Guard is ChannelParameters.GuardAuto ? 3 : channel.Guard & 3));
        d.Modify(274, 0x0030, (ushort)(partial * 0x20));

        ushort mask = MaskFor(channel, partial);
        DifferentialMask = mask;

        if (mask is not 0)
        {
            d.Modify(268, 0x0600, 0x0200);
        }
        else
        {
            d.Write(268, 0x0427);
        }

        for (int layer = 0; layer < 3; layer++)
        {
            d.Write(2 + layer, LayerWord(channel.Layers[layer]));
        }

        d.Write(0, (ushort)((channel.Mode * 0x2000) + mask));
        Differential = mask is not 0;
        int loop = WriteEqualiser(Densest(channel));
        d.Write(116, (ushort)loop);

        for (int index = 0; index < ControlLoopDefaults.Count; index++)
        {
            d.Write(ControlLoopFirst + index, (ushort)(ControlLoopDefaults[index] - (loop is 0 ? 355 : 0)));
        }

        WriteCoefficients(partial is 1 ? PartialReceptionCoefficients : FullBandCoefficients);
        d.Write(180, 0x0409);
        d.Write(187, 0x4102);

        for (int register = 181; register <= 186; register++)
        {
            d.Write(register, 0x2800);
        }

        d.Write(338, 0x1461);
        d.Write(340, 0x0206);
        d.Write(341, 0x0027);
        d.Write(228, 0);
        d.Write(265, 31);
        d.Write(205, 0x200F);
        d.Write(266, (ushort)(~AllSegments | mask | 0x0040));
        WriteCoefficients(ThirteenSegmentCoefficients);
        d.Write(352, mask);
        d.Write(353, AllSegments);
        d.Write(351, 0x00D5);
        d.Modify(1, 0xFFFC, 0x3FFC);
        WriteSegmentEdges(mask);

        ushort outside = (ushort)(~AllSegments | mask);
        d.Write(189, outside);
        d.Write(192, outside);
        d.Write(225, outside);
        d.Write(288, (ushort)(searching ? AllSegments : outside & AllSegments));
        d.Write(211, (ushort)(AllSegments & ~mask));
        d.Write(287, unchecked((ushort)(~AllSegments | 0x1000)));
        d.Write(178, 32);

        int weight = channel.Layers.Sum(layer =>
            (layer.Modulation is LayerModulation.Dqpsk ? 5 : 1) * layer.Segments);

        for (int register = 290; register <= 292; register++)
        {
            d.Write(register, (ushort)(weight * 128));
        }

        d.Write(250, 3285);
    }

    public static ushort LayerWord(LayerParameters layer)
    {
        int modulation = layer.Modulation switch
        {
            LayerModulation.Dqpsk => 0,
            LayerModulation.Qpsk => 1,
            LayerModulation.Qam16 => 2,
            _ => 3,
        };
        int codeRate = layer.CodeRate is 1 or 2 or 3 or 5 ? layer.CodeRate : 7;
        int interleave = layer.Interleave is 0 ? 0 : 32 - int.LeadingZeroCount(layer.Interleave);

        return (ushort)((modulation * 0x400) + ((layer.Segments & FourBits) * 0x40) + (codeRate * 8)
            + (interleave > 3 ? 0 : interleave));
    }

    private ushort MaskFor(ChannelParameters channel, int partial)
    {
        int differential(int first, int last) =>
            channel.Layers
                .Skip(first)
                .Take(last - first + 1)
                .Where(layer => layer.Modulation is LayerModulation.Dqpsk)
                .Sum(layer => layer.Segments);

        if (partial is 1)
        {
            int mask = channel.Layers[0].Modulation is LayerModulation.Dqpsk ? 1 << SegmentOrder[0] : 0;

            return (ushort)(mask | Run(1, differential(1, 2)));
        }

        return (ushort)(DifferentialMask | Run(0, differential(0, 2)));
    }

    private static int Run(int from, int count)
    {
        int mask = 0;

        for (int index = from; index < Math.Min(from + count, SegmentOrder.Count); index++)
        {
            mask |= 1 << SegmentOrder[index];
        }

        return mask;
    }

    private static LayerModulation Densest(ChannelParameters channel)
    {
        IEnumerable<LayerModulation> used = channel.Layers
            .Where(layer => layer.Segments >= 1)
            .Select(layer => layer.Modulation);

        if (used.Contains(LayerModulation.Qam64))
        {
            return LayerModulation.Qam64;
        }

        return used.Contains(LayerModulation.Qam16) ? LayerModulation.Qam16 : LayerModulation.Qpsk;
    }

    private int WriteEqualiser(LayerModulation densest)
    {
        (int loop, ushort[] values) = densest switch
        {
            LayerModulation.Qam64 => (7, new ushort[] { 0x0148, 0xFFF0, 0x00A4, 0xFFF8 }),
            LayerModulation.Qam16 => (7, new ushort[] { 0x023D, 0xFFDF, 0x00A4, 0xFFF0 }),
            _ => (0, new ushort[] { 0x099A, 0xFFAE, 0x0333, 0xFFF8 }),
        };

        for (int index = 0; index < values.Length; index++)
        {
            d.Write(215 + index, values[index]);
        }

        return loop;
    }

    private void WriteCoefficients(IReadOnlyList<short> coefficients)
    {
        for (int index = 0; index < coefficients.Count; index++)
        {
            d.Write(CoefficientsFirst + index, unchecked((ushort)coefficients[index]));
        }
    }

    private void WriteSegmentEdges(ushort mask)
    {
        int valid = AllSegments & ~mask;
        int left = 0;
        int right = 0;

        for (int segment = 0; segment < 13; segment++)
        {
            if ((valid & (1 << segment)) is 0)
            {
                continue;
            }

            if (segment is 0 || (valid & (1 << (segment - 1))) is 0)
            {
                left += 1 << segment;
            }

            if (segment is 12 || (valid & (1 << (segment + 1))) is 0)
            {
                right += 1 << segment;
            }
        }

        d.Write(222, (ushort)left);
        d.Write(223, (ushort)right);
    }

    private void Power(bool all)
    {
        ushort mode = d.Read(900);
        ushort clocks = d.Read(1280);
        d.Write(774, (ushort)(all ? 0 : 0x3FFF));
        d.Write(775, (ushort)(all ? 0 : 0xFFFF));
        d.Write(776, (ushort)(all ? 0 : 0xFFFF));
        d.Write(900, (ushort)((mode & ~0x0003) | (all ? 0 : 3)));
        d.Write(1280, (ushort)(clocks & 0x00FF));
    }

    private static DvbDeviceException Missing(string path, string what) =>
        new($"{path}: {what}, so the tuner is not the one this driver knows.", TuningFailure.DeviceUnusable);
}
