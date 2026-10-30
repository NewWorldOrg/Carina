using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tests;

public sealed class Dib0090Tests
{
    private const string Device = "/dev/bus/usb/001/002";

    [Fact]
    public void TheSynthesiserIsSetForTheChannelLessTheTunersOwnOffset()
    {
        var chip = new FakeChip(1);
        chip.Answer(0x1D, 300);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();
        var tuner = new Dib0090(chip, pacing);

        tuner.Tune(527_142, Device);

        Assert.Equal(527_079, tuner.RequestedKilohertz);
        Assert.Equal(527, chip.Last(0x15));
        Assert.Equal(0xFF06, chip.Last(0x16));
        Assert.Equal(6528, chip.Last(0x17));
        Assert.Equal(0x1C9E, chip.Last(0x19));
        Assert.Equal(0xFF2E, chip.Last(0x1C));
        Assert.Equal(0xB802, chip.WritesTo(0x0B)[0]);
        Assert.Equal(0x228F, chip.Last(0x24));
    }

    [Fact]
    public void TuningTheSameFrequencyAgainLeavesTheSynthesiserAlone()
    {
        var chip = new FakeChip(1);
        chip.Answer(0x1D, 300);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();
        var tuner = new Dib0090(chip, pacing);
        tuner.Tune(527_142, Device);
        chip.Written.Clear();

        tuner.Tune(527_142, Device);

        Assert.Empty(chip.WritesTo(0x15));
        Assert.NotEmpty(chip.WritesTo(0x18));
    }

    [Theory]
    [InlineData(0x0223, false)]
    [InlineData(0x022F, true)]
    public void TheTunersThisDriverKnowsAreRecognised(int identity, bool p1g)
    {
        Assert.Equal(p1g, Dib0090.Recognise((ushort)identity, Device));
    }

    [Theory]
    [InlineData(0x0222)]
    [InlineData(0x0203)]
    [InlineData(0x0323)]
    [InlineData(0x0225)]
    public void ATunerItDoesNotKnowIsRefused(int identity)
    {
        DvbDeviceException refused = UsbTestSupport.Refusal(() => Dib0090.Recognise((ushort)identity, Device));

        Assert.Equal(TuningFailure.DeviceUnusable, refused.Failure);
    }

    [Theory]
    [InlineData(0, 9)]
    [InlineData(50, 14)]
    [InlineData(90, 18)]
    [InlineData(100, 19)]
    public void AWaitInTenthsOfAMillisecondIsRoundedUpToSleep(int tenths, int milliseconds)
    {
        Assert.Equal(milliseconds, Dib0090.Milliseconds(tenths));
    }
}

public sealed class Dib8000Tests
{
    [Theory]
    [InlineData(LayerModulation.Qam64, 3, 2, 12, 0x0F1A)]
    [InlineData(LayerModulation.Qpsk, 2, 4, 1, 0x0453)]
    [InlineData(LayerModulation.Dqpsk, 0, 8, 0, 0x0038)]
    public void ALayerIsPackedIntoOneRegister(LayerModulation modulation, int codeRate, int interleave, int segments, int word)
    {
        Assert.Equal(word, Dib8000.LayerWord(new LayerParameters(modulation, codeRate, interleave, segments)));
    }

    [Fact]
    public void TheTunersOffsetIsTakenBackByTheDds()
    {
        var chip = new FakeChip(2);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        new Dib8000(chip, pacing).WriteDdsOffset(527_079, 527_142);

        Assert.Equal([(26, (ushort)0), (27, (ushort)0x01FE), (28, (ushort)0xCE5A)], chip.Written);
    }

    [Fact]
    public void ASearchWritesAllThirteenSegmentsWithoutDifferentialOnes()
    {
        var chip = new FakeChip(2);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();
        ChannelParameters searching = (ChannelParameters.Unknown with { Fft = FftSize.EightK, Guard = 2 })
            .WithLayerA(new LayerParameters(LayerModulation.Qam64, 2, 0, 13));

        new Dib8000(chip, pacing).WriteChannel(searching, 16, searching: true);

        Assert.Equal(256, chip.Last(10));
        Assert.Equal(0x6000, chip.Last(0));
        Assert.Equal(0x0427, chip.Last(268));
        Assert.Equal(0x0020, chip.Last(274));
        Assert.Equal(1, chip.Last(222));
        Assert.Equal(0x1000, chip.Last(223));
        Assert.Equal(0x1FFF, chip.Last(288));
        Assert.Equal(0xF000, chip.Last(287));
        Assert.Equal(13 * 128, chip.Last(290));
        Assert.Equal(7, chip.Last(116));
        Assert.Equal(396, chip.Last(117));
    }

    [Fact]
    public void TheSyncWaitFollowsTheFftAndTheGuard()
    {
        var chip = new FakeChip(2);
        chip.Registers[273] = 0x0006;
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        new Dib8000(chip, pacing).WriteSyncWait(ChannelParameters.Unknown with { Fft = FftSize.EightK, Guard = 2 });

        Assert.Equal((1584 * 16) | 0x0006, chip.Last(273));
    }
}

public sealed class DemodulatorSearchTests
{
    private const string Device = "/dev/bus/usb/001/002";

    private static readonly SearchTiming Timing = new(21_387_946, 527_079, 527_142);

    private static (DemodulatorSearch Search, FakeChip Chip, ManualTimeProvider Clock) Searching(FakeChip chip)
    {
        (DevicePacing pacing, ManualTimeProvider clock, _) = UsbTestSupport.Pacing();
        var demodulator = new Dib8000(chip, pacing);
        demodulator.Identify(Device);

        return (new DemodulatorSearch(chip, demodulator, pacing), chip, clock);
    }

    [Fact]
    public void AChannelThatSynchronisesIsFollowedToItsData()
    {
        FakeChip locking = UsbTestSupport.LockingChips().Demodulator;
        locking.Registers[1291] = 0x0205;
        (DemodulatorSearch search, FakeChip chip, _) = Searching(locking);

        SearchOutcome outcome = search.Run(Timing, Device);

        Assert.Equal(SearchOutcome.DataFlowing, outcome);
        Assert.Equal(FftSize.EightK, search.Written.Fft);
        Assert.Equal(2, search.Written.Guard);
        Assert.Equal(1, search.Written.PartialReception);
        Assert.Equal(new LayerParameters(LayerModulation.Qam64, 5, 1, 10), search.Written.Layers[1]);
        Assert.Equal(new LayerParameters(LayerModulation.Qam16, 1, 1, 3), search.Written.Layers[0]);
        Assert.Equal(0x0EA9, chip.Last(3));
    }

    [Fact]
    public void WithoutAWatchOnTheOutputALockIsEnough()
    {
        (DemodulatorSearch search, _, _) = Searching(UsbTestSupport.LockingChips().Demodulator);

        Assert.Equal(SearchOutcome.Locked, search.Run(Timing, Device));
    }

    [Fact]
    public void AGuardSearchThatFailsIsTriedAgainFromTheFft()
    {
        FakeChip chip = UsbTestSupport.LockingChips().Demodulator;
        chip.Answer(1284, 0x0001);
        (DemodulatorSearch search, _, _) = Searching(chip);

        SearchOutcome outcome = search.Run(Timing, Device);

        Assert.Equal(SearchOutcome.Failed, outcome);
        Assert.Contains((37, (ushort)0x0065), chip.Written);
    }

    [Fact]
    public void ADemodulatorThatNeverAnswersIsGivenUpOnAfterFiveSeconds()
    {
        FakeChip chip = UsbTestSupport.LockingChips().Demodulator;
        chip.Answer(1284, 0);
        (DemodulatorSearch search, _, ManualTimeProvider clock) = Searching(chip);

        DvbDeviceException refused = UsbTestSupport.Refusal(() => search.Run(Timing, Device));

        Assert.Equal(TuningFailure.Unspecified, refused.Failure);
        Assert.True(clock.GetUtcNow() - UsbTestSupport.Start > DemodulatorSearch.Patience);
    }
}

public sealed class CechZd1FrontendTests
{
    [Fact]
    public void TheClockRatioIsChosenToKeepItsHarmonicsOffTheChannel()
    {
        Assert.Equal(17, CechZd1Frontend.ChooseRatio(527_142));
    }

    [Fact]
    public void AFrequencyBelowUhfIsRefused()
    {
        (FakeChip demodulator, FakeChip tuner) = UsbTestSupport.LockingChips();
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();
        var frontend = new CechZd1Frontend(demodulator, tuner, pacing, "/dev/bus/usb/001/002");

        DvbDeviceException refused = UsbTestSupport.Refusal(() => frontend.Tune(189_142_857));

        Assert.Contains("UHF", refused.Message, StringComparison.Ordinal);
        Assert.Empty(demodulator.Written);
    }

    [Fact]
    public void ATuneThatLocksTurnsTheLedRed()
    {
        (FakeChip demodulator, FakeChip tuner) = UsbTestSupport.LockingChips();
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();
        var frontend = new CechZd1Frontend(demodulator, tuner, pacing, "/dev/bus/usb/001/002");
        frontend.Initialise();

        SearchOutcome outcome = frontend.Tune(BroadcastStandards.TerrestrialCentreHz(22));

        Assert.Equal(SearchOutcome.DataFlowing, outcome);
        Assert.Equal(0, demodulator.Registers[1029] & 0x0002);
        Assert.Equal(0x0002, demodulator.Registers[1030] & 0x0002);
        Assert.Equal(0x0400, demodulator.Last(1286));
        Assert.Equal(0x0111, demodulator.Last(901));
    }
}

public sealed class CechZd1SignalTests
{
    [Fact]
    public void TheCarrierToNoiseRatioIsTakenFromTheTwoFloatingValues()
    {
        double? decibels = CechZd1Signal.Decibels(
            CechZd1Signal.Expand((100 << 6) | 7),
            CechZd1Signal.Expand(100 << 6)
        );

        Assert.Equal(10 * Math.Log10(128), decibels!.Value, 6);
    }

    [Fact]
    public void ANegativeExponentShiftsLess()
    {
        Assert.Equal(1u << 15, CechZd1Signal.Expand((1 << 6) | 63));
    }

    [Fact]
    public void NoNoiseMeansNoReading()
    {
        Assert.Null(CechZd1Signal.Decibels(100, 0));
    }

    [Theory]
    [InlineData(65535 - 60000, -22.5)]
    [InlineData(65535 - 55953, -22.5)]
    [InlineData(65535 - 50000, -35.514)]
    public void TheSignalLevelIsReadOffTheCurve(int register, double dbm)
    {
        Assert.Equal(dbm, CechZd1Signal.StrengthDbm((ushort)register), 3);
    }

    [Fact]
    public void LockMeansSynchronisedWithDataOnALayer()
    {
        Assert.True(CechZd1Signal.StatusOf(0x10E0).HasFlag(FrontendStatus.Lock));
        Assert.False(CechZd1Signal.StatusOf(0x101E).HasFlag(FrontendStatus.Lock));
        Assert.False(CechZd1Signal.StatusOf(0x00E0).HasFlag(FrontendStatus.Lock));
    }

    [Fact]
    public void WithoutLockThereIsNoRatio()
    {
        CarrierToNoise reading = CechZd1Signal.CarrierToNoiseFrom(new SignalRegisters(0x001E, 1, 100, 0, 0x001E));

        Assert.Equal(SignalReading.FrontendNotLocked, reading.Reading);
    }
}

public sealed class CaptureUnpackerTests
{
    [Fact]
    public void TheFirstWholeUnitIsFoundAndItsStampDropped()
    {
        var unpacker = new CaptureUnpacker();
        var packets = new List<byte>();

        int taken = unpacker.Unpack(UsbTestSupport.Units(5, skip: 23), packets);

        Assert.Equal(4 * 188, taken);
        Assert.Equal(0x47, packets[0]);
        Assert.Equal(1, packets[3]);
        Assert.Equal(0x47, packets[188]);
        Assert.Equal(0, unpacker.Realignments);
    }

    [Fact]
    public void AUnitSplitAcrossReadsIsJoined()
    {
        var unpacker = new CaptureUnpacker();
        var packets = new List<byte>();
        byte[] stream = UsbTestSupport.Units(20, skip: 100);

        for (int at = 0; at < stream.Length; at += 100)
        {
            unpacker.Unpack(stream.AsSpan(at, Math.Min(100, stream.Length - at)), packets);
        }

        Assert.Equal(19 * 188, packets.Count);
        Assert.All(Enumerable.Range(0, 19), packet => Assert.Equal(0x47, packets[packet * 188]));
    }

    [Fact]
    public void LostBytesAreCountedAndTheUnitsFoundAgain()
    {
        var unpacker = new CaptureUnpacker();
        var packets = new List<byte>();
        byte[] stream = UsbTestSupport.Units(10);
        byte[] broken = [.. stream.AsSpan(0, 500), .. stream.AsSpan(600)];

        unpacker.Unpack(broken, packets);

        Assert.Equal(1, unpacker.Realignments);
        Assert.All(Enumerable.Range(0, packets.Count / 188), packet => Assert.Equal(0x47, packets[packet * 188]));
    }
}

public sealed class TransportCaptureTests
{
    private static (FakeBridge Bridge, BridgeMemory Memory) Widened()
    {
        var bridge = new FakeBridge();
        var memory = new BridgeMemory(bridge);
        AddressWidening.Widen(memory);

        return (bridge, memory);
    }

    [Fact]
    public void TheCoreIsHeldWhileItsProgramIsWrittenAndThenRun()
    {
        (FakeBridge bridge, BridgeMemory memory) = Widened();

        new TransportCapture(memory).Prepare();

        Fr30Program program = TransportCapture.CoreProgram();
        Assert.Equal([TransportCapture.CoreHeld, TransportCapture.CoreRunning], bridge.WritesAt(TransportCapture.FirstCore));
        Assert.Equal(TransportCapture.CoreProgramOrigin, bridge.Word(TransportCapture.FirstCore + TransportCapture.CoreEntry));
        Assert.Equal(program.Words[^1], bridge.Word(TransportCapture.FirstCore + TransportCapture.CoreProgramOrigin + (uint)(4 * (program.Words.Count - 1))));
        Assert.Equal(0x00060030u, bridge.Word(0xFDBC14));
        Assert.Equal(0x02000000u, bridge.Word(0xD01020));
    }

    [Fact]
    public void AProgramAlreadyInPlaceIsNotWrittenAgain()
    {
        (FakeBridge bridge, BridgeMemory memory) = Widened();
        new TransportCapture(memory).Prepare();
        uint origin = TransportCapture.FirstCore + TransportCapture.CoreProgramOrigin;
        bridge.Writes.Clear();

        new TransportCapture(memory).Prepare();

        Assert.DoesNotContain(bridge.Writes, write => write.Address >= origin && write.Address < origin + 0x1000);
    }

    [Fact]
    public void TheProgramBeginsBySettingTheCoresOwnRegisters()
    {
        Fr30Program program = TransportCapture.CoreProgram();

        Assert.Equal(0xC002C773u, program.Words[0]);
        Assert.True(program.Words.Count * 4 < 0x1000);
    }

    [Fact]
    public void StoppingHoldsTheCoreAndClosesTheInput()
    {
        (FakeBridge bridge, BridgeMemory memory) = Widened();

        new TransportCapture(memory).Stop();

        Assert.Equal(TransportCapture.CoreHeld, bridge.Word(TransportCapture.FirstCore));
        Assert.Equal(0u, bridge.Word(0xD01040));
        Assert.Equal(0x04000000u, bridge.Word(0xD02010));
    }
}

public sealed class CechZd1TunerDeviceTests
{
    private const string Device = "/dev/bus/usb/001/002";

    private static readonly long Channel22 = BroadcastStandards.TerrestrialCentreHz(22);

    private static (CechZd1TunerDevice Device, FakeBridge Bridge, FakeChip Demodulator, ManualTimeProvider Clock) Opened()
    {
        (FakeChip demodulator, FakeChip tuner) = UsbTestSupport.LockingChips();
        FakeBridge bridge = UsbTestSupport.BridgeWith(demodulator, tuner);
        (DevicePacing pacing, ManualTimeProvider clock, _) = UsbTestSupport.Pacing();
        bridge.Idle = clock.Advance;
        CechZd1TunerDevice device = CechZd1TunerDevice.Open(
            new FakeUsbHost(bridge),
            Device,
            Channel22,
            pacing,
            TimeSpan.FromSeconds(5)
        );

        return (device, bridge, demodulator, clock);
    }

    [Fact]
    public void OpeningTunesAndStartsTheStreamWithReadsAlreadyWaiting()
    {
        (CechZd1TunerDevice device, FakeBridge bridge, _, _) = Opened();

        Assert.Equal(((byte)0x81, 64_512, 16), bridge.BulkOpened);
        Assert.Equal(0x02000000u, bridge.Word(0xD02010));
        Assert.Equal(0x03000000u, bridge.Word(0xD01040));
        device.Dispose();
    }

    [Fact]
    public void ReadsHandOnWholePacketsWithoutTheirStamps()
    {
        (CechZd1TunerDevice device, FakeBridge bridge, _, _) = Opened();
        bridge.Transfers.Enqueue(UsbTestSupport.Units(50, skip: 30));

        byte[] read = device.Read(188 * 10, CancellationToken.None);

        Assert.Equal(188 * 10, read.Length);
        Assert.Equal(0x47, read[0]);
        Assert.Equal(0x47, read[188 * 9]);
        device.Dispose();
    }

    [Fact]
    public void ALockedTunerThatSendsNothingSaysSo()
    {
        (CechZd1TunerDevice device, _, _, _) = Opened();

        DvbDeviceException refused = UsbTestSupport.Refusal(() => device.Read(188, CancellationToken.None));

        Assert.Equal(TuningFailure.LockedWithoutData, refused.Failure);
        device.Dispose();
    }

    [Fact]
    public void TheCarrierToNoiseRatioIsMeasuredWhileLocked()
    {
        (CechZd1TunerDevice device, _, _, _) = Opened();

        SignalQuality quality = device.Measure();

        Assert.True(quality.HasLock);
        Assert.True(quality.CarrierToNoise.TryGetDecibels(out double decibels));
        Assert.Equal(10 * Math.Log10(64), decibels, 6);
        Assert.Equal(SignalReading.NotImplementedByThisTuner, quality.PostViterbiErrors.Reading);
        device.Dispose();
    }

    [Fact]
    public void ClosingStopsTheCoreTheReadsAndTheLed()
    {
        (CechZd1TunerDevice device, FakeBridge bridge, FakeChip demodulator, _) = Opened();

        device.Dispose();

        Assert.Equal(TransportCapture.CoreHeld, bridge.Word(TransportCapture.FirstCore));
        Assert.True(bridge.BulkClosed);
        Assert.True(bridge.Disposed);
        Assert.Equal(0x0002, demodulator.Registers[1029] & 0x0002);
        Assert.Equal(0, demodulator.Last(1286));
    }

    [Fact]
    public void AChannelThatDoesNotSynchroniseIsNoLockAndLetsTheDeviceGo()
    {
        (FakeChip demodulator, FakeChip tuner) = UsbTestSupport.LockingChips();
        demodulator.Answer(1284, 0x0001);
        FakeBridge bridge = UsbTestSupport.BridgeWith(demodulator, tuner);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        DvbDeviceException refused = UsbTestSupport.Refusal(
            () => CechZd1TunerDevice.Open(new FakeUsbHost(bridge), Device, Channel22, pacing, TimeSpan.FromSeconds(5))
        );

        Assert.Equal(TuningFailure.NoLock, refused.Failure);
        Assert.True(bridge.Disposed);
        Assert.Null(bridge.BulkOpened);
    }

    [Fact]
    public void AnotherDeviceOnThePathIsNotOpened()
    {
        var host = new FakeUsbHost(new FakeBridge()) { Model = new UsbModel(0x1234, 0x5678) };
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        UsbTestSupport.Refusal(() => CechZd1TunerDevice.Open(host, Device, Channel22, pacing, TimeSpan.FromSeconds(5)));

        Assert.Equal(0, host.Opened);
    }
}

public sealed class UsbTunerDetectorTests
{
    [Fact]
    public void OnlyTheTunerItKnowsIsOfferedAndItsIdFollowsThePort()
    {
        var host = new FakeUsbHost(new FakeBridge());
        host.Nodes.Add(new UsbDeviceNode("/dev/bus/usb/001/002", CechZd1TunerDevice.Model, "1-1"));
        host.Nodes.Add(new UsbDeviceNode("/dev/bus/usb/001/003", new UsbModel(0x1234, 0x5678), "1-2"));

        IReadOnlyList<TunerDetection> found = UsbTunerDetector.Using(host).Detect();

        TunerDetection tuner = Assert.Single(found);
        Assert.Equal("usb1-1", tuner.DeviceId);
        Assert.Equal([DeviceKind.Terrestrial], tuner.Receives);
        Assert.Equal(DeviceDetection.Detected, tuner.Detection);
        Assert.Equal("/dev/bus/usb/001/002", tuner.DevicePath);
    }
}

public sealed class UsbBackendConfigurationTests
{
    private static string Configuration(string kind) => $$"""
        {
          "socketPath": "/run/carina/driver.sock",
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "tuner": { "backend": "usb" },
          "devices": [{ "id": "usb1-1", "kind": "{{kind}}", "devicePath": "/dev/bus/usb/001/002" }]
        }
        """;

    [Fact]
    public void TheUsbBackendIsReadFromTheConfiguration()
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read(Configuration("terrestrial"));

        Assert.Empty(result.Problems);
        Assert.Equal(TunerBackend.Usb, result.Configuration!.Tuner!.Backend);
    }

    [Fact]
    public void ASatelliteDeviceOnTheUsbBackendIsRefused()
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read(Configuration("satellite"));

        Assert.Contains(result.Problems, problem => problem.StartsWith("devices[0].kind:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheUsbBackendHasItsOwnDetector()
    {
        DriverConfiguration configuration = DriverConfigurationReader.Read(Configuration("terrestrial")).Configuration!;

        Assert.IsType<UsbTunerDetector>(TunerDetectors.For(configuration));
    }
}

public sealed class UsbDevicePathTests
{
    private const string Recorded = "/dev/bus/usb/001/002";
    private const string Replugged = "/dev/bus/usb/001/005";

    private static readonly DeviceSettings Tuner = new("usb1-1", DeviceKind.Terrestrial, Recorded);

    private static readonly UsbModel Stranger = new(0x1234, 0x5678);

    private static DriverConfiguration Configuration() =>
        new(
            OutputRoots: [new OutputRootSettings("primary", "/srv/recordings")],
            Tuner: new TunerSettings(TunerBackend.Usb),
            Devices: [Tuner]
        );

    private static (TunerDeviceFactory Factory, FakeUsbHost Host) Factory(params UsbDeviceNode[] nodes)
    {
        (FakeChip demodulator, FakeChip tuner) = UsbTestSupport.LockingChips();
        var host = new FakeUsbHost(UsbTestSupport.BridgeWith(demodulator, tuner));
        host.Nodes.AddRange(nodes);
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        return (TunerDeviceFactory.Using(Configuration(), host, pacing), host);
    }

    [Fact]
    public void AReplugIntoTheSamePortIsOpenedUnderTheSameId()
    {
        (TunerDeviceFactory factory, FakeUsbHost host) = Factory(
            new UsbDeviceNode(Replugged, CechZd1TunerDevice.Model, "1-1")
        );

        using ITunerDevice device = factory.Create(
            Tuner,
            new TuningRequest(TunerKind.Terrestrial, 22),
            null,
            HeadHold.EveryProgramme
        );

        Assert.Equal([Replugged], host.OpenedPaths);
    }

    [Fact]
    public void ADeviceThatTookTheOldNodeIsNotMistakenForTheTuner()
    {
        (TunerDeviceFactory factory, FakeUsbHost host) = Factory(
            new UsbDeviceNode(Recorded, Stranger, "1-2"),
            new UsbDeviceNode(Replugged, CechZd1TunerDevice.Model, "1-1")
        );

        factory.Check(Tuner);

        Assert.Equal([Replugged], host.OpenedPaths);
    }

    [Fact]
    public void ATunerOnAnotherPortIsNotTakenForThisOne()
    {
        (TunerDeviceFactory factory, FakeUsbHost host) = Factory(
            new UsbDeviceNode(Recorded, CechZd1TunerDevice.Model, "1-1"),
            new UsbDeviceNode(Replugged, CechZd1TunerDevice.Model, "1-2")
        );

        factory.Check(Tuner with { Id = "usb1-2", DevicePath = Recorded });

        Assert.Equal([Replugged], host.OpenedPaths);
    }

    [Fact]
    public void AnIdNoPortAnswersToFallsBackToTheRecordedNode()
    {
        (TunerDeviceFactory factory, FakeUsbHost host) = Factory();

        factory.Check(Tuner with { Id = "torne" });

        Assert.Equal([Recorded], host.OpenedPaths);
    }

    [Fact]
    public void ADeviceThatIsNotTheTunerOnTheRecordedNodeIsStillRefused()
    {
        (TunerDeviceFactory factory, FakeUsbHost host) = Factory(new UsbDeviceNode(Recorded, Stranger, "1-2"));

        UsbTestSupport.Refusal(() => factory.Check(Tuner));

        Assert.Empty(host.OpenedPaths);
    }
}
