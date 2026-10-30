using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// The tuner behind the demodulator. Tuning runs as a machine of steps, each of which names how long to wait, in
/// tenths of a millisecond, before the next.
/// </summary>
public sealed class Dib0090(IRegisterBus t, DevicePacing pacing)
{
    public const int MostSteps = 1000;

    public const int UhfRequestOffsetKilohertz = 63;

    private const ushort Awake = 0x4000;
    private const int ReferenceKilohertz = 2000;
    private const int FractionScale = 6528;
    private const int FourBits = 0b1111;
    private const int FiveBits = 0b1_1111;
    private const int DcSwitch = 31;

    private Phase phase;
    private int asked;
    private int? lastWidebandGain;
    private ushort bb1;
    private bool tunedOnce;
    private int fraction;
    private TunerBandRow band = Dib0090Tables.Bands[^1];
    private ushort widebandSwitch;
    private ushort w10;
    private ushort b13;
    private ushort before;
    private int step;
    private int value;
    private int best;
    private int closest;

    private bool dcPending;
    private bool widebandPending;
    private bool temperaturePending;
    private bool capacitorPending;

    private enum Phase
    {
        Start,
        WidebandRead,
        TemperatureSecond,
        TemperatureThird,
        TemperatureFourth,
        CapacitorWrite,
        CapacitorRead,
        CapacitorEnd,
        CapacitorSelfRead,
        CapacitorSelfEnd,
        StageZero,
        StageOne,
        Done,
    }

    public ushort Identity { get; private set; }

    public bool P1g { get; private set; }

    public int Package => (Identity >> 5) & 0x7;

    public int RequestedKilohertz { get; private set; }

    public int WidebandOffset { get; private set; }

    public int Temperature { get; private set; }

    public static int Milliseconds(int tenths) => 10 * (tenths + 99) / 100;

    public void Initialise(Dib8000 demodulator, string path)
    {
        demodulator.Gpio(0, high: false);
        pacing.SleepMilliseconds(10);
        demodulator.Gpio(5, high: false);
        pacing.SleepMilliseconds(10);
        demodulator.Gpio(5, high: true);
        pacing.SleepMilliseconds(10);

        t.Write(0x24, 0x0022);
        t.Write(0x1B, 0x00A2);
        t.Write(0x20, 0x3B00);
        t.Write(0x23, 0x0A30);
        t.Modify(0x21, 0, 0x8000);
        Identity = (ushort)(t.Read(0x1A) & ~0x0800);
        P1g = Recognise(Identity, path);
        t.Write(0x1B, (ushort)((t.Read(0x1A) & 0x0040) is not 0 ? 0x00EB : 0x00A2));

        for (int index = 0; index < Dib0090Tables.Defaults.Count; index++)
        {
            t.Write(0x01 + index, Dib0090Tables.Defaults[index]);
        }

        t.Write(0x1C, 0xFF2D);
        t.Write(0x39, 0x0000);
        t.Write(0x1E, 0x07FF);
        t.Write(DcSwitch, 0x0007);
        t.Write(0x24, 0x22EB);
        t.Write(0x3C, 0x03FF);
        t.Write(0x3D, 0x0111);

        if (P1g)
        {
            t.Write(0x05, 0xABCD);
            t.Write(0x11, 0x00B4);
            t.Write(0x1C, 0xFFFD);
            t.Write(0x40, 0x0108);
        }

        if ((Identity & FiveBits) >= 3)
        {
            ReadFuses();
        }

        t.Write(0x14, 2);
        dcPending = true;
        widebandPending = true;
        temperaturePending = true;
    }

    public static bool Recognise(ushort identity, string path)
    {
        int version = identity & FiveBits;
        int package = (identity >> 5) & 0x7;
        bool known = ((identity >> 8) & FourBits) is 2
            && (identity & 0x3) is not 2
            && package is 1 or 4 or 6 or 7
            && version is 15 or 7 or 3 or 1;

        return known
            ? version is 15 or 7
            : throw new DvbDeviceException(
                $"{path}: the tuner identifies itself as 0x{identity:X4}, which this driver does not drive.",
                TuningFailure.DeviceUnusable
            );
    }

    public void Wake() => t.Modify(0x23, 0, Awake);

    public void RequestTemperature() => temperaturePending = true;

    public void CalibrateDc()
    {
        t.Modify(0x23, Awake, 0);
        ushort bb6 = 0;
        ushort bb7 = 0x040D;
        t.Write(0x24, (ushort)(t.Read(0x24) & 0x0FFB));
        t.Modify(0x10, 0x07F8, 0x003B);
        t.Modify(0x23, Awake, 0);

        foreach (DcRow row in DcRows)
        {
            ushort first = P1g ? row.FirstOnP1g : row.First;
            t.Write(0x01, first);
            t.Write(0x07, (ushort)(bb7 + (row.Iq * 128)));
            pacing.SleepMilliseconds(Milliseconds(50));
            CalibrateDcRow(row, ref bb6, ref bb7);
        }

        t.Write(0x07, (ushort)(bb7 & ~0x0008));
        t.Write(DcSwitch, 7);
        dcPending = false;
    }

    public void Tune(int kilohertz, string path)
    {
        asked = kilohertz;
        phase = Phase.Start;

        for (int taken = 0; taken < MostSteps; taken++)
        {
            int wait = Step();
            pacing.SleepMilliseconds(Milliseconds(wait));

            if (phase is Phase.Done)
            {
                return;
            }
        }

        throw DvbFailure.Refused($"{path}: the tuner did not finish tuning within {MostSteps} steps.");
    }

    public int WidebandTarget(int megahertz)
    {
        WidebandDetectorRow row = Dib0090Tables.DetectorFor(megahertz);
        t.Write(0x10, (ushort)((widebandSwitch & ~(0b111 << 13)) | (row.Gain << 13)));
        int hot = (short)(row.HotBase - ((row.HotSlope * megahertz) >> 6));
        int cold = (short)(row.ColdBase - ((row.ColdSlope * megahertz) >> 6));
        cold += ((hot - cold) * Math.Clamp(Temperature, 0, 128)) >> 7;

        return (short)(WidebandOffset + cold);
    }

    public void WritePwmRamps()
    {
        IReadOnlyList<int> radio = Dib0090Tables.RadioFrequencyVhfUhf.Values;
        IReadOnlyList<int> baseband = Dib0090Tables.Baseband.Values;
        t.Write(0x2A, 0xFFFF);

        for (int index = 0; index < 6; index++)
        {
            t.Write(0x2C + index, (ushort)radio[3 + index]);
        }

        t.Write(0x3E, (ushort)radio[9]);
        t.Write(0x3F, (ushort)radio[10]);
        bb1 = (ushort)(baseband[0] > 500 ? bb1 | 0x2000 : bb1 & ~0x2000);
        t.Write(0x33, 0xFFFF);

        for (int index = 0; index < 4; index++)
        {
            t.Write(0x35 + index, (ushort)baseband[3 + index]);
        }

        int on = radio[0] is 0 ? 0 : 1;
        t.Write(0x32, (ushort)((on * 0x1000) + (on * 0x800)));
        t.Write(0x04, 0x0001);
        t.Write(0x39, 0x0400);
    }

    public void WriteGainControl(ushort value) => t.Write(0x04, value);

    private int Step()
    {
        if (phase is not Phase.Start)
        {
            return Continue();
        }

        if (dcPending || widebandPending || temperaturePending)
        {
            t.Modify(0x23, Awake, 0);
        }

        if (dcPending)
        {
            CalibrateDc();
        }

        if (widebandPending)
        {
            return WidebandFirst();
        }

        if (temperaturePending)
        {
            return TemperatureFirst();
        }

        return capacitorPending ? CapacitorFirst() : BodyStart();
    }

    private int Continue() =>
        phase switch
        {
            Phase.WidebandRead => WidebandSecond(),
            Phase.TemperatureSecond => TemperatureSecond(),
            Phase.TemperatureThird => TemperatureThird(),
            Phase.TemperatureFourth => TemperatureFourth(),
            Phase.CapacitorWrite => CapacitorWrite(),
            Phase.CapacitorRead => CapacitorRead(),
            Phase.CapacitorEnd => CapacitorEnd(),
            Phase.CapacitorSelfRead => CapacitorSelfRead(),
            Phase.CapacitorSelfEnd => CapacitorSelfEnd(),
            Phase.StageZero => StageZero(),
            Phase.StageOne => StageOne(),
            _ => throw new InvalidOperationException($"The tuner has no step for {phase}."),
        };

    private int Next(Phase following, int wait)
    {
        phase = following;

        return wait;
    }

    private int WidebandFirst()
    {
        int megahertz = (RequestedKilohertz is 0 ? asked : RequestedKilohertz) / 1000;
        int gain = Dib0090Tables.DetectorFor(megahertz).Gain;
        gain = gain is 0 ? 4 : gain;

        if (gain == lastWidebandGain)
        {
            widebandPending = false;

            return Next(Phase.Start, 0);
        }

        t.Write(0x10, (ushort)(0x1B81 + 0x0400 + (gain * 0x2000) + 0x0008));
        t.Write(0x24, 0x02EB);
        lastWidebandGain = gain;

        return Next(Phase.WidebandRead, 90);
    }

    private int WidebandSecond()
    {
        WidebandOffset = t.Read(0x1D);
        widebandPending = false;

        return Next(Phase.Start, 0);
    }

    private int TemperatureFirst()
    {
        w10 = t.Read(0x10);
        t.Write(0x10, (ushort)((w10 & ~0x07F8) | 0x0040));
        b13 = t.Read(0x13);
        t.Write(0x13, (ushort)(b13 | 0x0300));

        return Next(Phase.TemperatureSecond, 15);
    }

    private int TemperatureSecond()
    {
        before = t.Read(0x1D);
        t.Write(0x13, (ushort)((b13 & ~0x0300) | 0x0200));

        return Next(Phase.TemperatureThird, 15);
    }

    private int TemperatureThird()
    {
        ushort after = t.Read(0x1D);
        Temperature = ((short)((after - before) * 180) >> 8) + 55;

        return Next(Phase.TemperatureFourth, 15);
    }

    private int TemperatureFourth()
    {
        t.Write(0x13, b13);
        t.Write(0x10, w10);
        temperaturePending = false;

        return Next(Phase.Start, 15);
    }

    private int BodyStart()
    {
        int request = asked - UhfRequestOffsetKilohertz;

        if (request == RequestedKilohertz)
        {
            capacitorPending = true;

            return Next(Phase.Start, 20);
        }

        tunedOnce = false;
        band = Dib0090Tables.BandFor(request, P1g);
        SynthesiserRow synthesiser = Dib0090Tables.SynthesiserFor(request, P1g);
        t.Write(0x0B, (ushort)(0xB800 + band.Switch));
        WriteSynthesiser(request, synthesiser);
        t.Write(0x24, (ushort)((band.Enable & ~0x0060) | 0x0002 | 0x0006));
        RequestedKilohertz = request;
        capacitorPending = true;

        return Next(Phase.Start, 20);
    }

    private void WriteSynthesiser(int request, SynthesiserRow row)
    {
        long divided = (long)row.Divider * request * 2 / row.Prescaler;
        int whole = (int)(divided / ReferenceKilohertz);
        int remainder = (int)(divided - ((long)whole * ReferenceKilohertz));

        if (remainder < 100)
        {
            remainder = 0;
        }
        else if (remainder < 200)
        {
            remainder = 200;
        }
        else if (remainder > ReferenceKilohertz - 100)
        {
            remainder = 0;
            whole++;
        }
        else if (remainder > ReferenceKilohertz - 200)
        {
            remainder = ReferenceKilohertz - 200;
        }

        fraction = remainder * FractionScale / 200;
        int sign = P1g ? row.DividerSignOnP1g : row.DividerSign;
        int lo5 = LoopFilter(row.VcoBand) | (sign * 0x800) | (row.VcoBand * 0x80);
        int lo6 = P1g || fraction is 0 ? 0xFFF8 : 0xFF28;
        int denominator = fraction is 0 ? 1 : 255;
        lo6 |= fraction is 0 ? 0 : 0x0006;

        t.Write(0x15, (ushort)whole);
        t.Write(0x16, (ushort)((denominator * 0x100) + 6));
        t.Write(0x17, (ushort)fraction);
        t.Write(0x19, (ushort)lo5);
        t.Write(0x1C, (ushort)lo6);
    }

    private int LoopFilter(int vcoBand)
    {
        if (P1g)
        {
            return 0x042C;
        }

        if (fraction is 0)
        {
            return vcoBand is 1 ? 0x049F : 0x041F;
        }

        return vcoBand is 1 ? 0x049E : 0x041D;
    }

    private int CapacitorFirst()
    {
        t.Write(0x10, 0x02B1);
        t.Write(0x1E, 0x0032);

        if (P1g)
        {
            return Next(Phase.CapacitorSelfRead, 0);
        }

        step = tunedOnce ? 4 : 64;
        value = tunedOnce ? t.Read(0x18) & 0x7F : 64;
        best = value;
        closest = 3000;

        return Next(Phase.CapacitorWrite, 0);
    }

    private int CapacitorWrite()
    {
        step /= 2;
        t.Write(0x18, (ushort)(0xE900 + value));

        return Next(Phase.CapacitorRead, 0);
    }

    private int CapacitorRead()
    {
        int read = t.Read(0x1D);
        int target = fraction is 0 ? 200 : 400;
        int distance = Math.Abs(read - target);
        int direction = read >= target ? -1 : 1;

        if (distance < closest)
        {
            closest = distance;
            best = value;
        }

        value += direction * step;

        return Next(step >= 1 ? Phase.CapacitorWrite : Phase.CapacitorEnd, 25);
    }

    private int CapacitorEnd()
    {
        t.Write(0x18, (ushort)(0xE900 + best));
        capacitorPending = false;

        return Next(Phase.StageZero, 0);
    }

    private int CapacitorSelfRead()
    {
        t.Write(0x40, 0x01FF);
        t.Read(0x40);

        return Next(Phase.CapacitorSelfEnd, 50);
    }

    private int CapacitorSelfEnd()
    {
        t.Write(0x40, 0x018C);
        t.Read(0x40);
        t.Read(0x18);
        capacitorPending = false;

        return Next(Phase.StageZero, 0);
    }

    private int StageZero()
    {
        t.Write(0x1E, 0x07FF);
        int gain = Dib0090Tables.DetectorFor(RequestedKilohertz / 1000).Gain;
        widebandSwitch = (ushort)((gain * 0x2000) + 0x1800 + 0x0781 + 0x0002);
        t.Write(0x10, widebandSwitch);

        if (band.Enable is Dib0090Tables.CBandEnable && P1g)
        {
            t.Write(0x09, (ushort)band.LnaBias);
            t.Write(0x0B, (ushort)(0xB800 + (band.LnaTune * 0x40) + band.Switch));
        }
        else
        {
            t.Write(0x09, (ushort)((band.LnaTune * 0x20) + band.LnaBias));
        }

        t.Write(0x0C, (ushort)band.VoltageToCurrent);
        t.Write(0x0D, (ushort)band.Mixer);
        t.Write(0x0E, (ushort)band.Load);

        return Next(Phase.StageOne, 10);
    }

    private int StageOne()
    {
        bb1 = (ushort)((bb1 & 0x3FFF) | 0x8000);
        t.Write(0x01, bb1);
        t.Write(0x03, 0x6008);
        t.Write(0x04, 0x0001);
        t.Write(0x02, 0x2A16);
        t.Write(0x05, 0xABCD);
        tunedOnce = true;
        widebandPending = true;
        temperaturePending = false;

        return Next(Phase.Done, 10);
    }

    private void ReadFuses()
    {
        ushort e2 = t.Read(0x26);
        ushort e4 = t.Read(0x28);

        if ((Identity & 0xFF) is 0x03 or 0x07 || e2 is 0xFFFF)
        {
            t.Write(0x22, 0x0010);
            int calibration = (t.Read(0x22) >> 6) & 0x3FF;
            calibration = calibration < 670 || calibration is 1023 ? 850 : calibration;
            int n0 = 165 - ((calibration * 10) >> 6);
            e2 = (ushort)(0x3000 + (34 * 64) + n0);
            e4 = e2;
        }

        if (e2 != e4)
        {
            e2 = (ushort)(e2 & e4);
        }

        if (e2 is 0xFFFF)
        {
            return;
        }

        int c = e2 & 0x3F;
        int h = (e2 >> 6) & 0x3F;
        int n = (e2 >> 12) & FourBits;
        c = c is >= 40 or <= 9 ? 32 : c + 14;
        h = h is >= 40 or <= 25 ? 34 : h;
        n = n is >= 8 or <= 0 ? 3 : n;
        t.Write(0x13, (ushort)(h * 1024));
        t.Write(0x02, (ushort)((n * 2048) + ((h >> 2) * 64) + c));
    }

    private void CalibrateDcRow(DcRow row, ref ushort bb6, ref ushort bb7)
    {
        int stage = 0;
        int smallest = 1023;

        while (true)
        {
            WriteDcAdjustment(row, stage, ref bb6, ref bb7);
            t.Write(DcSwitch, 7);
            pacing.SleepMilliseconds(Milliseconds(100));
            ushort first = t.Read(0x1D);
            t.Write(DcSwitch, 4);
            pacing.SleepMilliseconds(Milliseconds(100));
            int difference = (short)(first - t.Read(0x1D));

            if (stage is 0 && difference < 0)
            {
                smallest = -1023;
            }

            if (stage is 0 && (row.Sign is 1 ? difference < 0 : difference > 0))
            {
                stage = 0x10;
            }

            if ((difference < 0) == (smallest < 0) && Signed(stage) < 15)
            {
                stage++;
                smallest = difference;

                continue;
            }

            if (Math.Abs(difference) > Math.Abs(smallest))
            {
                stage--;
            }

            WriteDcAdjustment(row, stage, ref bb6, ref bb7);

            return;
        }
    }

    private void WriteDcAdjustment(DcRow row, int stage, ref ushort bb6, ref ushort bb7)
    {
        bool seventh = row.Register is 0x07;
        ushort basis = seventh ? bb7 : bb6;
        ushort adjusted = (ushort)((basis & ~(FiveBits << row.Shift)) | ((stage & FiveBits) << row.Shift));
        t.Write(row.Register, adjusted);

        if (seventh)
        {
            bb7 = adjusted;
        }
        else
        {
            bb6 = adjusted;
        }
    }

    private static int Signed(int stage) => stage > 15 ? stage - 16 : stage;

    private sealed record DcRow(int Register, int Shift, int Sign, ushort First, ushort FirstOnP1g, int Iq);

    private static readonly IReadOnlyList<DcRow> DcRows =
    [
        new(0x06, 5, 1, 0x20D0, 0x2078, 1),
        new(0x07, 11, 1, 0x20D0, 0x2078, 0),
        new(0x06, 0, 0, 0x3DD0, 0x3D78, 1),
        new(0x06, 10, 0, 0x3DD0, 0x3D78, 0),
    ];
}
