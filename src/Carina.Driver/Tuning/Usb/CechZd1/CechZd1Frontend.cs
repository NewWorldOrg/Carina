using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// The demodulator and the tuner taken together: bringing them up, tuning a channel and putting them to sleep.
/// </summary>
public sealed class CechZd1Frontend
{
    public const int LowestUhfKilohertz = 250_001;

    public const int HighestKilohertz = 860_000;

    public const int RatioAtStart = 18;

    public static readonly IReadOnlyDictionary<int, uint> TimingFrequencies = new Dictionary<int, uint>
    {
        [17] = 21_387_946,
        [18] = 20_199_727,
        [19] = 19_136_583,
        [20] = 18_179_756,
    };

    private static readonly IReadOnlyList<(int Register, ushort Value)> Clocking =
    [
        (906, 0x2D98),
        (901, 0x0112),
        (902, 0x050F),
        (902, 0x0507),
        (904, 0x1E80),
        (23, 0x0337),
        (24, 0xF980),
        (27, 0),
        (28, 0),
        (26, 0),
        (922, 0xD257),
        (898, 0x0004),
        (1029, 0xFFFF),
        (1030, 0),
        (1032, 0xFFFF),
        (1037, 0),
    ];

    private static readonly IReadOnlyList<(int First, ushort[] Values)> Defaults =
    [
        (7, [0x0004, 0x0400, 0x0814]),
        (11, [0x001B, 0x7740, 0x005B, 0x8D80, 0x01C9, 0xC380, 0x0000, 0x0080, 0x0000, 0x0090, 0x0001, 0xD4C0]),
        (80, [7250, 7238, 7264, 7309, 7338, 7382, 7427, 7456, 7500, 7544, 7574]),
        (108, [0, 0, 0, 0]),
        (175, [0x0410]),
        (179, [8192]),
        (181, [0x2800, 0x2800, 0x2800, 0x2800, 0x2800, 0x2800]),
        (193, [0x0666, 0x0000]),
        (205, [0x200F, 15]),
        (215, [0x023D, 0x00A4, 0x00A4, 0x7FF0, 0x3CCC]),
        (230, [0]),
        (263, [0x0800]),
        (268, [0x0427]),
        (270, [0x0001]),
        (285, [0x0020]),
        (299, [0x0062]),
        (338, [0x1461]),
    ];

    private readonly IRegisterBus d;
    private readonly DevicePacing pacing;
    private readonly string path;
    private bool asleep;

    public CechZd1Frontend(IRegisterBus demodulatorBus, IRegisterBus tunerBus, DevicePacing pacing, string path)
    {
        d = demodulatorBus;
        this.pacing = pacing;
        this.path = path;
        Demodulator = new Dib8000(demodulatorBus, pacing);
        Tuner = new Dib0090(tunerBus, pacing);
    }

    public Dib8000 Demodulator { get; }

    public Dib0090 Tuner { get; }

    public int AgcReference { get; private set; } = Dib8000.AgcReferenceBeforeAnyTune;

    public ChannelParameters Channel { get; private set; } = ChannelParameters.Unknown;

    public void Initialise()
    {
        Demodulator.Identify(path);
        d.Write(1025, 0x0100);
        d.Write(1287, 0x0003);
        d.Write(1025, 0x0100);
        d.Write(1028, 0);
        Demodulator.PowerAll();
        Demodulator.AdcOff();
        WriteAll([(770, 0xFFFF), (771, 0xFFFF), (772, 0xFFFC), (898, 0x000C), (1280, 0x004D), (1281, 0x000C)]);
        WriteAll([(770, 0), (771, 0), (772, 0), (898, 0x0004), (1280, 0), (1281, 0)]);
        WriteAll(Clocking);
        Demodulator.TransportOutput(on: false);
        d.Write(40, 0x0755);

        foreach ((int first, ushort[] values) in Defaults)
        {
            for (int index = 0; index < values.Length; index++)
            {
                d.Write(first + index, values[index]);
            }
        }

        d.Write(903, 0x0031);
        d.Modify(1285, 0x0002, 0);
        Demodulator.WriteTimingFrequency(TimingFrequencies[RatioAtStart]);
        Demodulator.AdcSlowOn();
        d.Write(923, 0);
        d.Write(924, 776);
        d.Write(923, 1);
        d.Write(923, 0);
        pacing.SleepMilliseconds(1);
        Demodulator.AdcSlowOff();
        d.Modify(285, 0, 0x0060);
        Demodulator.PowerInterfaceOnly();
        d.Read(565);
        d.Modify(285, 0, 0x0060);
        d.Write(1028, 0);
        d.Write(1025, 0xC000);
        Tuner.Initialise(Demodulator, path);
        Wake();
        Tuner.CalibrateDc();
        Demodulator.TransportOutput(on: true);
    }

    public SearchOutcome Tune(long hertz)
    {
        int kilohertz = (int)(hertz / 1000);

        if (kilohertz is < LowestUhfKilohertz or > HighestKilohertz)
        {
            throw DvbFailure.Refused(
                $"{path}: this tuner is driven on UHF only, {LowestUhfKilohertz} to {HighestKilohertz} kHz, and {kilohertz} kHz is outside it."
            );
        }

        if (asleep)
        {
            Wake();
        }

        Demodulator.SetDiversity(Diversity.Both);
        Demodulator.TransportOutput(on: false);
        Tuner.RequestTemperature();
        uint timing = ConfigureTheTuner(kilohertz);
        Demodulator.SetDiversity(Diversity.InternalOnly);
        StartTheAgc();

        var search = new DemodulatorSearch(d, Demodulator, pacing);
        SearchOutcome outcome = search.Run(new SearchTiming(timing, Tuner.RequestedKilohertz, kilohertz), path);
        Channel = search.Written;
        Demodulator.TransportOutput(on: true);

        if (outcome is not SearchOutcome.Failed)
        {
            Demodulator.LedRed();
        }

        return outcome;
    }

    public void Sleep()
    {
        Demodulator.Gpio(0, high: true);
        Demodulator.TransportOutput(on: false);
        Demodulator.PowerInterfaceOnly();
        Demodulator.AdcSlowOff();
        Demodulator.AdcOff();
        asleep = true;
    }

    public SignalRegisters ReadSignal()
    {
        ushort before = d.Read(Dib8000.Status);
        ushort noise = d.Read(542);
        ushort signal = d.Read(543);
        ushort strength = d.Read(390);

        return new SignalRegisters(before, noise, signal, strength, d.Read(Dib8000.Status));
    }

    public static int ChooseRatio(int kilohertz)
    {
        long scaled = (long)kilohertz * 256 / 1000;
        int chosen = 20;
        long farthest = 0;

        for (int ratio = 17; ratio <= 20; ratio++)
        {
            long harmonic = 12L * ratio * 256 / 16;
            long remainder = scaled % harmonic;
            remainder = remainder > harmonic / 2 ? harmonic - remainder : remainder;

            if (remainder > 717 && remainder > farthest)
            {
                chosen = ratio;
                farthest = remainder;
            }
        }

        return chosen;
    }

    public static int AgcReferenceFor(int widebandTarget) =>
        Math.Min(4095, ((widebandTarget * 8 * 18 / 33) + 1) / 2);

    private void Wake()
    {
        Demodulator.PowerAll();
        Demodulator.AdcOn();
        Demodulator.AdcSlowOn();
        Demodulator.Gpio(0, high: false);
        Tuner.Wake();
        asleep = false;
    }

    private uint ConfigureTheTuner(int kilohertz)
    {
        Demodulator.Gpio(3, high: false);
        Tuner.Tune(kilohertz, path);

        ushort clock = d.Read(1856);
        int ratio = RatioAtStart;

        if ((clock & 0x3F) is not 1 || ((clock >> 6) & 0x3F) is not RatioAtStart)
        {
            ratio = ChooseRatio(kilohertz);
            d.Write(901, (ushort)(0x0100 + ratio));
        }

        uint timing = TimingFrequencies[ratio];
        Demodulator.WriteTimingFrequency(timing);
        AgcReference = AgcReferenceFor(Tuner.WidebandTarget(kilohertz / 1000));
        d.Write(106, (ushort)AgcReference);
        Tuner.WritePwmRamps();

        return timing;
    }

    private void StartTheAgc()
    {
        Demodulator.AdcOn();
        Demodulator.WriteAgc(cBand: false, AgcReference);
        pacing.SleepMilliseconds(Dib0090.Milliseconds(70));
        Tuner.WriteGainControl(0);
        d.Write(770, 0x0A00);
        d.Write(770, 0);
        pacing.SleepMilliseconds(Dib0090.Milliseconds(50));
        pacing.SleepMilliseconds(Dib0090.Milliseconds(70));
        Tuner.WriteGainControl(1);
        Tuner.WriteGainControl(1);
    }

    private void WriteAll(IReadOnlyList<(int Register, ushort Value)> writes)
    {
        foreach ((int register, ushort value) in writes)
        {
            d.Write(register, value);
        }
    }
}
