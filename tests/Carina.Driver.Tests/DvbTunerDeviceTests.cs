using Carina.Driver.Configuration;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbTunerDeviceTests
{
    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    private static readonly DvbDevicePaths Paths = new(
        "/dev/dvb/adapter0/frontend0",
        "/dev/dvb/adapter0/demux0",
        "/dev/dvb/adapter0/dvr0"
    );

    private static readonly DvbTunerSettings Settings = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(5),
        8 * 1024 * 1024
    );

    [Fact]
    public void OpeningTakesTheFrontendThenTheDemuxThenTheReader()
    {
        var (calls, clock) = Ready();

        using var device = Open(calls, clock);

        Assert.Equal(
            ["/dev/dvb/adapter0/frontend0", "/dev/dvb/adapter0/demux0", "/dev/dvb/adapter0/dvr0"],
            calls.Opened.Select(node => node.Path)
        );
        Assert.Equal(DvbAccess.Control, calls.Opened[0].Access);
        Assert.Equal(DvbAccess.Control, calls.Opened[1].Access);
        Assert.Equal(DvbAccess.Stream, calls.Opened[2].Access);
    }

    [Fact]
    public void OpeningAsksForTheConfiguredRingBufferAndTheFullStreamFilter()
    {
        var (calls, clock) = Ready();

        using var device = Open(calls, clock);

        Assert.Equal(8 * 1024 * 1024, Assert.Single(calls.BufferSizesSet));
        Assert.Equal(DemuxFilter.EverythingFromTheFrontend(), Assert.Single(calls.FiltersSet));
    }

    [Fact]
    public void AFrontendThatNeverLocksIsReportedAsAFailureToLockNotAsAnEmptyStream()
    {
        var (calls, clock) = Ready();
        calls.ReportStatus(FrontendStatus.Signal);

        var refusal = Assert.Throws<DvbDeviceException>(() => Open(calls, clock));

        Assert.Contains("did not lock", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("no bytes will follow", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrontendThatNeverLocksLeavesNoDescriptorBehind()
    {
        var (calls, clock) = Ready();
        calls.ReportStatus(FrontendStatus.Signal);

        Assert.Throws<DvbDeviceException>(() => Open(calls, clock));

        Assert.Equal(calls.Opened.Select(node => node.Descriptor).Order(), calls.Closed.Order());
    }

    [Fact]
    public void ADemuxThatWillNotFilterLeavesNoDescriptorBehind()
    {
        var (calls, clock) = Ready();
        calls.RefuseFilterWith = Errno.NoSuchDevice;

        Assert.Throws<DvbDeviceException>(() => Open(calls, clock));

        Assert.Equal(calls.Opened.Select(node => node.Descriptor).Order(), calls.Closed.Order());
    }

    [Fact]
    public void ATerrestrialTuneNeverTouchesTheAerialSupply()
    {
        var (calls, clock) = Ready();

        using var device = Open(calls, clock);

        Assert.Empty(calls.VoltagesSet);
    }

    [Fact]
    public void ASatelliteTuneAlwaysStatesTheAerialSupplyRatherThanLeavingItAsFound()
    {
        var (calls, clock) = Ready();

        using var device = DvbTunerDevice.Open(
            calls,
            clock,
            Paths,
            DvbChannel.BroadcastSatellite(1, 16_400),
            LnbPower.For(DeviceKind.Satellite, enabledInTheLedger: false),
            Settings,
            CancellationToken.None
        );

        Assert.Equal(LnbVoltage.Off, Assert.Single(calls.VoltagesSet));
    }

    [Fact]
    public void BytesComeBackFromTheReader()
    {
        var (calls, clock) = Ready();
        calls.Deliver([1, 2, 3, 4]);

        using var device = Open(calls, clock);

        Assert.Equal([1, 2, 3, 4], device.Read(4, CancellationToken.None));
    }

    [Fact]
    public void AShortReadReturnsOnlyWhatArrived()
    {
        var (calls, clock) = Ready();
        calls.Deliver([1, 2, 3]);

        using var device = Open(calls, clock);

        Assert.Equal([1, 2, 3], device.Read(8, CancellationToken.None));
    }

    [Fact]
    public void ALockedTunerThatDeliversNothingIsADistinctFailureFromNotLocking()
    {
        var (calls, clock) = Ready();

        using var device = Open(calls, clock);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => device.Read(188, CancellationToken.None)
        );

        Assert.Contains("locked but no transport stream bytes", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("/dev/dvb/adapter0/dvr0", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverrunRingBufferIsCountedRatherThanEndingTheSession()
    {
        var (calls, clock) = Ready();
        calls.Polls.Enqueue(SyscallOutcome.Ok(1));
        calls.Reads.Enqueue(SyscallOutcome.Failed(Errno.Overflowed));
        calls.Deliver([9, 9, 9]);

        using var device = Open(calls, clock);

        Assert.Equal([9, 9, 9], device.Read(3, CancellationToken.None));
        Assert.Equal(1, device.Overflows);
    }

    [Fact]
    public void AReaderThatStopsAnsweringEndsTheSessionByName()
    {
        var (calls, clock) = Ready();
        calls.Polls.Enqueue(SyscallOutcome.Ok(1));
        calls.Reads.Enqueue(SyscallOutcome.Failed(Errno.NoSuchDevice));

        using var device = Open(calls, clock);

        var refusal = Assert.Throws<DvbDeviceException>(
            () => device.Read(188, CancellationToken.None)
        );

        Assert.Contains("reading transport stream bytes", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("errno 19", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadStopsWhenTheSessionIsCancelled()
    {
        var (calls, clock) = Ready();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var device = Open(calls, clock);

        Assert.Throws<OperationCanceledException>(() => device.Read(188, cancellation.Token));
    }

    [Fact]
    public void ClosingStopsTheFilterAndHandsBackEveryDescriptor()
    {
        var (calls, clock) = Ready();
        var device = Open(calls, clock);

        device.Dispose();
        device.Dispose();

        Assert.Equal(1, calls.FiltersStopped);
        Assert.Equal(calls.Opened.Select(node => node.Descriptor).Order(), calls.Closed.Order());
    }

    [Fact]
    public void TheAerialSupplyIsOffUnlessTheLedgerSaysOtherwiseForASatelliteTuner()
    {
        Assert.Equal(LnbVoltage.Off, LnbPower.For(DeviceKind.Satellite, false));
        Assert.Equal(LnbVoltage.Eighteen, LnbPower.For(DeviceKind.Satellite, true));
        Assert.Equal(LnbVoltage.Off, LnbPower.For(DeviceKind.Terrestrial, true));
        Assert.Equal(LnbVoltage.Off, LnbPower.For(DeviceKind.Unspecified, true));
    }

    private static (ScriptedDvbSystemCalls Calls, ManualTimeProvider Clock) Ready()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);

        return (calls, clock);
    }

    private static DvbTunerDevice Open(ScriptedDvbSystemCalls calls, ManualTimeProvider clock) =>
        DvbTunerDevice.Open(
            calls,
            clock,
            Paths,
            DvbChannel.Terrestrial(27),
            LnbVoltage.Off,
            Settings,
            CancellationToken.None
        );
}
