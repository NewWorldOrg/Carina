using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

public enum SearchOutcome
{
    Failed = 0,

    Locked = 1,

    DataFlowing = 2,
}

public sealed record SearchTiming(uint TimingFrequency, int TunerKilohertz, int AskedKilohertz);

/// <summary>
/// Walks the demodulator from a search for the transmission mode to a lock and data on the output, one stage at a
/// time. A stage that stays where it is looks again ten milliseconds later.
/// </summary>
public sealed class DemodulatorSearch(IRegisterBus d, Dib8000 demodulator, DevicePacing pacing)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private const int SmallestGapMilliseconds = 10;
    private const ushort SearchFailed = 0x0001;
    private const ushort FftFound = 0x0002;
    private const ushort TmccSynchronised = 0x001E;
    private const ushort Synchronised = 0x1800;
    private const ushort Settled = 0x0400;
    private const ushort LayerData = 0x00E0;
    private const ushort DemodulatorRunning = 0x0002;
    private const int FiveBits = 0b1_1111;
    private const int FourBits = 0b1111;
    private const int SixBits = 0b11_1111;

    private static readonly TimeSpan Symbol = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 10);

    private ChannelParameters channel = ChannelParameters.Unknown;
    private Looking looking;
    private bool readBack;
    private DateTimeOffset timeout;
    private SearchOutcome? outcome;

    private enum Looking
    {
        Fft,
        Guard,
        Done,
    }

    private enum Stage
    {
        Begin,
        SearchStart,
        One,
        Two,
        Three,
        Four,
        Five,
        Six,
        Seven,
        Eight,
        Nine,
        Ten,
    }

    public ChannelParameters Written { get; private set; } = ChannelParameters.Unknown;

    public SearchOutcome Run(SearchTiming timing, string path)
    {
        DateTimeOffset deadline = pacing.Now + Patience;
        bool guardOnly = demodulator.Version is Dib8000.LatestVersion;
        SearchOutcome first = Once(guardOnly, timing, deadline, path);

        return first is SearchOutcome.Failed && guardOnly ? Once(false, timing, deadline, path) : first;
    }

    private SearchOutcome Once(bool guardOnly, SearchTiming timing, DateTimeOffset deadline, string path)
    {
        channel = guardOnly ? ChannelParameters.Unknown with { Fft = FftSize.EightK } : ChannelParameters.Unknown;
        looking = guardOnly ? Looking.Guard : Looking.Fft;
        readBack = false;
        outcome = null;
        Stage stage = Stage.Begin;

        while (true)
        {
            if (pacing.Now > deadline)
            {
                throw DvbFailure.Refused(
                    $"{path}: the demodulator neither locked nor gave up within {Patience.TotalSeconds:0} seconds."
                );
            }

            (Stage next, int wait) = Advance(stage, timing);

            if (outcome is { } finished)
            {
                return finished;
            }

            stage = next;
            int gap = (int)Math.Ceiling(wait / 10.0);
            pacing.SleepMilliseconds(Math.Max(gap, SmallestGapMilliseconds));
        }
    }

    private (Stage Next, int Wait) Advance(Stage stage, SearchTiming timing) =>
        stage switch
        {
            Stage.Begin => Begin(timing),
            Stage.SearchStart => StartSearching(),
            Stage.One => StageOne(),
            Stage.Two => StageTwo(),
            Stage.Three => StageThree(),
            Stage.Four => StageFour(),
            Stage.Five => StageFive(),
            Stage.Six => Fail(),
            Stage.Seven => StageSeven(),
            Stage.Eight => StageEight(),
            Stage.Nine => StageNine(),
            _ => StageTen(),
        };

    private (Stage, int) Begin(SearchTiming timing)
    {
        d.Read(565);
        d.Modify(771, 0, DemodulatorRunning);
        d.Write(285, (ushort)(d.Read(285) & 0x0060));
        demodulator.WriteDdsOffset(timing.TunerKilohertz, timing.AskedKilohertz);
        demodulator.WriteTimingFrequency(timing.TimingFrequency);

        return (Stage.SearchStart, 0);
    }

    private (Stage, int) StartSearching()
    {
        if (demodulator.Version is Dib8000.LatestVersion && looking is Looking.Fft)
        {
            StartLookingForTheFft();
        }
        else if (demodulator.Version is Dib8000.LatestVersion)
        {
            StartLookingForTheGuard();
        }
        else
        {
            StartOnAnOlderVersion();
        }

        return (Stage.One, 15);
    }

    private void StartLookingForTheFft()
    {
        d.Write(37, 0x0065);
        d.Write(116, 0);
        d.Modify(0, 0xE000, 0x8000);
        d.Modify(1, 0x0003, 0);
        d.Write(6, 0);
        d.Write(7, 0);
        d.Write(8, 0);
        d.Modify(10, 0xFDFF, 0x0100);
        WriteWaits(20, 20, 20);
        d.Write(17, 0);
        d.Write(18, 200);
        d.Write(19, 0);
        d.Write(20, 400);
        d.WriteLong(21, Dib8000.ClockHertz * 20);
        d.Modify(32, 0x0F00, 0x0900);
        d.Write(355, 2);
        d.Write(356, 0);
        d.Write(357, 0x0111);
        ushort control = d.Read(770);
        d.Write(770, (ushort)((control & 0xDFFF) | 0x2000));
        d.Write(770, (ushort)(control & 0xDFFF));
        d.Modify(0, 0xF800, 0x2000);
    }

    private void StartLookingForTheGuard()
    {
        channel = (channel with { Guard = ChannelParameters.GuardEighth, Inversion = 0 }).WithLayerA(SearchLayer);
        demodulator.WriteChannel(channel, 16, searching: true);
        d.Write(6, 0x0004);
        d.Write(7, 0x0008);
        d.Write(8, 0x1000);
        WriteWaits(50, 200, 1000);
        d.Write(355, 3);
        d.Write(356, 0);
        d.Write(357, 0x000F);
        FinishStarting();
    }

    private void StartOnAnOlderVersion()
    {
        channel = (channel with { Inversion = 0 }).WithLayerA(SearchLayer);
        bool fftUnknown = channel.Fft is FftSize.Auto;
        bool guardUnknown = channel.Guard is ChannelParameters.GuardAuto;
        int sequence = (fftUnknown, guardUnknown) switch
        {
            (true, true) => 7,
            (false, true) => 3,
            (true, false) => 2,
            _ => 0,
        };
        channel = channel with
        {
            Fft = fftUnknown ? FftSize.EightK : channel.Fft,
            Guard = guardUnknown ? ChannelParameters.GuardEighth : channel.Guard,
        };

        if (fftUnknown)
        {
            d.Modify(0, 0x6000, 0x2000);
        }

        demodulator.WriteChannel(channel, sequence, searching: true);
        d.Write(6, 0x0004);
        d.Write(7, 0x0008);
        d.Write(8, 0x1000);
        WriteWaits(50, 100, 1000);
        FinishStarting();
    }

    private static readonly LayerParameters SearchLayer = new(LayerModulation.Qam64, 2, 0, 13);

    private void FinishStarting()
    {
        ushort control = d.Read(0);
        d.Write(0, (ushort)(control | 0x8000));
        d.Read(1284);
        d.Write(0, control);
    }

    private void WriteWaits(int first, int second, int third)
    {
        d.WriteLong(11, (uint)(Dib8000.ClockHertz * first));
        d.WriteLong(13, (uint)(Dib8000.ClockHertz * second));
        d.WriteLong(15, (uint)(Dib8000.ClockHertz * third));
    }

    private (Stage, int) StageOne()
    {
        ushort state = d.Read(1284);

        if (looking is Looking.Fft && demodulator.Version is Dib8000.LatestVersion)
        {
            return ((state & SearchFailed) is not 0 ? Stage.Two : Stage.One, 0);
        }

        if ((state & SearchFailed) is not 0)
        {
            return Stop();
        }

        if ((state & FftFound) is 0)
        {
            return (Stage.One, 0);
        }

        if (!readBack)
        {
            readBack = true;
            ReadBack();
        }

        if (looking is Looking.Guard)
        {
            return (Stage.Two, 0);
        }

        looking = Looking.Done;

        return (Stage.Three, 0);
    }

    private (Stage, int) StageTwo()
    {
        if (looking is Looking.Fft)
        {
            uint c2 = d.ReadLong(594);
            uint c1 = d.ReadLong(596);
            uint c0 = d.ReadLong(598);
            channel = channel with { Fft = FftFrom(c0, c1, c2) };
            looking = Looking.Guard;

            return (Stage.SearchStart, 10);
        }

        if (looking is Looking.Guard)
        {
            d.Read(570);

            return (Stage.Three, 0);
        }

        return Stop();
    }

    private static FftSize FftFrom(uint c0, uint c1, uint c2)
    {
        if (c0 >= c1 && c0 >= c2)
        {
            return FftSize.TwoK;
        }

        return c1 >= c2 ? FftSize.FourK : FftSize.EightK;
    }

    private (Stage, int) StageThree()
    {
        Written = channel;
        demodulator.WriteLoopValues(channel.Mode, afterLock: false);
        demodulator.WriteChannel(channel, 0, searching: false);

        return (Stage.Four, 0);
    }

    private (Stage, int) StageFour()
    {
        d.Write(770, 0x4000);
        d.Write(770, 0);
        demodulator.WriteSyncWait(channel);
        demodulator.SetDiversity(Diversity.InternalOnly);
        int symbols = 2 * ((d.Read(180) >> 6) & SixBits);
        timeout = pacing.Now + (Symbol * symbols);

        return (Stage.Five, 0);
    }

    private (Stage, int) StageFive()
    {
        ushort status = d.Read(Dib8000.Status);

        if ((status & Synchronised) is 0)
        {
            return (pacing.Now > timeout ? Stage.Six : Stage.Five, 0);
        }

        demodulator.WriteTimingFrequency(d.ReadLong(435));

        if (demodulator.DifferentialMask is not 0)
        {
            return (Stage.Eight, 0);
        }

        timeout = pacing.Now + (Symbol * (20 * ((d.Read(188) >> 5) & FiveBits)));

        return (Stage.Seven, 0);
    }

    private (Stage, int) StageSeven()
    {
        if ((d.Read(Dib8000.Status) & Settled) is not 0)
        {
            return (Stage.Eight, 14);
        }

        return (pacing.Now > timeout ? Stage.Six : Stage.Seven, 0);
    }

    private (Stage, int) StageEight()
    {
        d.Modify(771, DemodulatorRunning, 0);
        demodulator.WriteLoopValues(channel.Mode, afterLock: true);

        return (Stage.Nine, 0);
    }

    private (Stage, int) StageNine()
    {
        if ((d.Read(1291) & 0x0200) is 0)
        {
            outcome = SearchOutcome.Locked;

            return (Stage.Nine, 0);
        }

        int interleave = channel.Layers.Any(layer => layer.Segments >= 1) ? channel.Layers[0].Interleave : 0;
        int waits = interleave switch
        {
            0 => 2,
            3 => 8,
            _ => 2 * interleave,
        };
        timeout = pacing.Now + TimeSpan.FromMilliseconds(200 * waits);

        return (Stage.Ten, 0);
    }

    private (Stage, int) StageTen()
    {
        bool flowing = (d.Read(Dib8000.Status) & LayerData) is not 0;

        if (flowing)
        {
            outcome = SearchOutcome.DataFlowing;
        }
        else if (pacing.Now > timeout)
        {
            outcome = SearchOutcome.Failed;
        }

        return (Stage.Ten, 0);
    }

    private (Stage, int) Fail()
    {
        d.Modify(771, DemodulatorRunning, 0);
        demodulator.WriteLoopValues(channel.Mode, afterLock: true);
        outcome = SearchOutcome.Failed;

        return (Stage.Six, 0);
    }

    private (Stage, int) Stop()
    {
        outcome = SearchOutcome.Failed;

        return (Stage.Six, 0);
    }

    private void ReadBack()
    {
        if ((d.Read(Dib8000.Status) & TmccSynchronised) != TmccSynchronised)
        {
            return;
        }

        d.Read(508);
        ushort mode = d.Read(570);
        int partial = d.Read(505) & 1;
        var layers = new LayerParameters[3];

        for (int layer = 0; layer < 3; layer++)
        {
            int interleave = d.Read(499 + layer) & 3;
            int codeRate = d.Read(481 + layer) & 7;
            layers[layer] = new LayerParameters(
                (LayerModulation)((d.Read(487 + layer) & 3) + 1),
                codeRate is 1 or 2 or 3 or 5 ? codeRate : 7,
                interleave is 3 ? 4 : interleave,
                d.Read(493 + layer) & FourBits
            );
        }

        channel = new ChannelParameters(
            ((mode >> 4) & 3) switch
            {
                1 => FftSize.TwoK,
                2 => FftSize.FourK,
                _ => FftSize.EightK,
            },
            mode & 3,
            ((mode >> 6) & 1) ^ 1,
            partial,
            layers
        );
    }
}
