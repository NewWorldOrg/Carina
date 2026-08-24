using Carina.Contracts;
using Carina.Driver.Diagnostics;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerSessionTests : IDisposable
{
    private const int ChunkSize = TsPacketReader.PacketLength * 4;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("carina-session-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private TunerSession Session(
        ITunerDevice device,
        ManualTimeProvider clock,
        IRecordingWriter? writer = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TimeSpan? runsFor = null,
        DiagnosticsStore? diagnostics = null,
        SignalQualityWatch? watch = null
    ) =>
        new(
            SessionId.Parse("s-1"),
            purpose,
            "adapter0",
            device,
            Start,
            Start + (runsFor ?? TimeSpan.FromHours(1)),
            clock,
            writer,
            ChunkSize,
            diagnostics: diagnostics,
            watch: watch
        );

    private RecordingWriter Writer(string name = "s-1") => new(root, name);

    [Fact]
    public void TheSessionPassesOnWhatTheDeviceLostToRingBufferOverruns()
    {
        var device = new ScriptedTunerDevice();
        using TunerSession session = Session(device, new ManualTimeProvider(Start));

        Assert.Equal(0, session.DeviceOverflows);

        device.Overflows = 3;

        Assert.Equal(3, session.DeviceOverflows);
    }

    [Fact]
    public void ASessionOnATunerThatWasAlreadyOverrunCountsOnlyItsOwnLosses()
    {
        var device = new ScriptedTunerDevice { Overflows = 5 };
        using TunerSession session = Session(device, new ManualTimeProvider(Start));

        Assert.Equal(0, session.DeviceOverflows);

        device.Overflows = 7;

        Assert.Equal(2, session.DeviceOverflows);
    }

    [Fact]
    public void TheQualityIsReadWhileTheSessionRunsAndNotOnlyWhenItWasTuned()
    {
        var clock = new ManualTimeProvider(Start);
        var signal = new ScriptedQualitySource();
        var device = new PacedTunerDevice { Signal = signal };
        using TunerSession session = Session(device, clock, Writer(), watch: Watch());

        var steps = new Pacer(device);

        session.Start();
        steps.Read(1);

        Assert.Equal(1, signal.Reads);

        clock.Advance(WatchInterval);
        steps.Read(1);

        Assert.Equal(2, signal.Reads);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void HoweverManyChunksArriveTheQualityIsReadOnlyOncePerInterval()
    {
        var clock = new ManualTimeProvider(Start);
        var signal = new ScriptedQualitySource();
        var device = new PacedTunerDevice { Signal = signal };
        using TunerSession session = Session(device, clock, Writer(), watch: Watch());

        var steps = new Pacer(device);

        session.Start();
        steps.Read(6);

        Assert.Equal(6, device.Reads);
        Assert.Equal(1, signal.Reads);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void ASessionWhoseFrontendLosesTheLockSaysSoWhileItIsStillRunning()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource signal = new ScriptedQualitySource().Answer(
            Readings.Measured(),
            Readings.WithoutLock()
        );
        var device = new PacedTunerDevice { Signal = signal };
        var told = new List<SignalQualitySample>();
        using TunerSession session = Session(
            device,
            clock,
            Writer(),
            watch: Watch((_, sample) => told.Add(sample))
        );

        var steps = new Pacer(device);

        session.Start();
        steps.Read(1);
        clock.Advance(WatchInterval);
        steps.Read(1);

        Assert.Single(told);
        Assert.Equal(1, session.LockLosses);
        Assert.Equal(SessionState.Active, session.State);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void ARecordingThatLostTheLockIsStillWritingWhenTheLossIsReported()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource signal = new ScriptedQualitySource().Answer(
            Readings.Measured(),
            Readings.WithoutLock()
        );
        var device = new PacedTunerDevice { Signal = signal };
        using TunerSession session = Session(device, clock, Writer(), watch: Watch());

        var steps = new Pacer(device);

        session.Start();
        steps.Read(1);
        clock.Advance(WatchInterval);
        steps.Read(2);

        Assert.Equal(1, session.LockLosses);
        Assert.Equal(3 * ChunkSize, session.BytesRecorded);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void ALostLockIsWrittenIntoTheLedgerRatherThanOnlyIntoTheLog()
    {
        var clock = new ManualTimeProvider(Start);
        var diagnostics = new DiagnosticsStore(clock);
        ScriptedQualitySource signal = new ScriptedQualitySource().Answer(Readings.WithoutLock());
        var device = new PacedTunerDevice { Signal = signal };
        using TunerSession session = Session(
            device,
            clock,
            Writer(),
            diagnostics: diagnostics,
            watch: Watch()
        );

        var steps = new Pacer(device);

        session.Start();
        steps.Read(1);

        DiagnosticSnapshot reported = Assert.Single(diagnostics.Snapshot());

        Assert.Equal(DiagnosticReason.TuningLost, reported.Reason);
        Assert.Equal("adapter0", reported.DeviceId);
        Assert.Equal(SessionId.Parse("s-1"), reported.SessionId);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void AFrontendThatWillNotAnswerCostsAFaultAndNotTheSession()
    {
        var clock = new ManualTimeProvider(Start);
        var signal = new ScriptedQualitySource { RefuseFromReadNumber = 1 };
        var device = new PacedTunerDevice { Signal = signal };
        using TunerSession session = Session(device, clock, Writer(), watch: Watch());

        var steps = new Pacer(device);

        session.Start();
        steps.Read(1);

        Assert.Equal(SessionState.Active, session.State);
        Assert.True(session.FaultCount > 0);
        Assert.False(session.Quality?.Readable);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void ASessionOnATunerThatMeasuresNothingReadsNoQualityAtAll()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer(), watch: Watch());

        var steps = new Pacer(device);

        session.Start();
        steps.Read(2);

        Assert.Null(session.Quality);
        Assert.Equal(0, session.LockLosses);

        session.Stop();
        WaitForEnd(session);
    }

    private static void WaitForEnd(TunerSession session) =>
        session.WaitForEnd(TimeSpan.FromSeconds(10));

    private static void WaitUntilRecording(TunerSession session)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (session.BytesRecorded is 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        Assert.True(session.BytesRecorded > 0);
    }

    [Fact]
    public void ASessionEndsItselfWhenItsEndTimeArrives()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        WaitUntilRecording(session);
        clock.Advance(TimeSpan.FromHours(2));
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(SessionStopReason.EndTimeReached, session.StopReason);
    }

    [Fact]
    public void ADeliberateStopEndsTheSessionAsStopped()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(SessionStopReason.Requested, session.StopReason);
    }

    [Fact]
    public void ADrainCapStopIsNotMistakenForADeliberateStop()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop(SessionStopReason.DrainCapReached);
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.DrainCapReached, session.StopReason);
        Assert.NotNull(session.FailureCause);
    }

    [Fact]
    public void ADeviceFailureEndsTheSessionAsFailedAndNotStopped()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(failAfterReads: 3), clock, Writer());

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.DeviceFailed, session.StopReason);
        Assert.IsType<IOException>(session.FailureCause);
    }

    [Fact]
    public void ADeviceThatStopsProducingBytesIsAFailureAndNotAnEnding()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(emptyAfterReads: 3), clock, Writer());

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.IsType<EndOfStreamException>(session.FailureCause);
    }

    [Fact]
    public void AWriteFailureEndsTheSessionAsFailedAndNotStopped()
    {
        var clock = new ManualTimeProvider(Start);
        RecordingWriter writer = Writer();
        writer.Dispose();

        using TunerSession session = Session(new ScriptedTunerDevice(), clock, writer);

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.RecordingFailed, session.StopReason);
    }

    [Fact]
    public void AWriteFailureIsARecordingFailureAndNotADeviceOne()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);
        var device = new ScriptedTunerDevice();

        using TunerSession session = Session(
            device,
            clock,
            new BrittleRecordingWriter(Path.Combine(root, "s-1.ts")),
            diagnostics: store
        );

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.RecordingFailed, session.StopReason);
        Assert.IsType<IOException>(session.FailureCause);
        Assert.True(device.Disposed);

        DiagnosticSnapshot entry = Assert.Single(
            store.Snapshot(),
            candidate => candidate.Reason is DiagnosticReason.RecordingWriteFailed
        );

        Assert.Equal("s-1", entry.SessionId.Value);
        Assert.Equal("adapter0", entry.DeviceId);
        Assert.Contains("No space left on device", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureToCloseTheRecordingIsWrittenToTheDiagnostics()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);
        var writer = new CountingRecordingWriter(
            Path.Combine(root, "s-1.ts"),
            failOnClose: true
        );

        TunerSession session = Session(new ScriptedTunerDevice(), clock, writer, diagnostics: store);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Contains(
            store.Snapshot(),
            candidate => candidate.Reason is DiagnosticReason.RecordingWriteFailed
                && candidate.SessionId.Value is "s-1"
        );
    }

    [Fact]
    public void ADeviceFailureLeavesADeviceFaultedDiagnostic()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);

        using TunerSession session = Session(
            new ScriptedTunerDevice(failAfterReads: 3),
            clock,
            diagnostics: store
        );

        session.Start();
        WaitForEnd(session);

        DiagnosticSnapshot entry = Assert.Single(
            store.Snapshot(),
            candidate => candidate.Reason is DiagnosticReason.DeviceFaulted
        );

        Assert.Equal("adapter0", entry.DeviceId);
        Assert.Equal("s-1", entry.SessionId.Value);
    }

    [Fact]
    public void ADrainCapStopLeavesARecordingCutShortDiagnostic()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);

        using TunerSession session = Session(
            new ScriptedTunerDevice(),
            clock,
            Writer(),
            diagnostics: store
        );

        session.Start();
        session.Stop(SessionStopReason.DrainCapReached);
        WaitForEnd(session);

        DiagnosticSnapshot entry = Assert.Single(
            store.Snapshot(),
            candidate => candidate.Reason is DiagnosticReason.RecordingCutShort
        );

        Assert.Equal("s-1", entry.SessionId.Value);
    }

    [Fact]
    public void ACleanEndLeavesNoDiagnostic()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);

        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer(), diagnostics: store);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void TheFirstMeasurementFaultIsADiagnosticAndTheRestAreNot()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);
        TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer(), diagnostics: store);

        session.Ended += _ => throw new InvalidOperationException("the first listener is broken");
        session.Ended += _ => throw new InvalidOperationException("the second listener is broken");
        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(2, session.FaultCount);
        Assert.Single(
            store.Snapshot(),
            candidate => candidate.Reason is DiagnosticReason.MeasurementFaulted
        );
    }

    [Fact]
    public void AFailureToCloseTheRecordingIsAFailedSessionAndNotASilentSuccess()
    {
        var clock = new ManualTimeProvider(Start);
        var writer = new CountingRecordingWriter(
            Path.Combine(root, "s-1.ts"),
            failOnClose: true
        );
        var device = new ScriptedTunerDevice();
        TunerSession session = Session(device, clock, writer);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.RecordingFailed, session.StopReason);
        Assert.IsType<IOException>(session.FailureCause);
        Assert.True(device.Disposed);
    }

    [Fact]
    public void AFailureToCloseTheRecordingStillReleasesTheDeviceAndAnnouncesTheEnd()
    {
        var clock = new ManualTimeProvider(Start);
        var writer = new CountingRecordingWriter(
            Path.Combine(root, "s-1.ts"),
            failOnClose: true
        );
        var device = new ScriptedTunerDevice();
        TunerSession session = Session(device, clock, writer);
        int announced = 0;

        session.Ended += _ => Interlocked.Increment(ref announced);
        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.True(device.Disposed);
        Assert.Equal(1, announced);
    }

    [Fact]
    public void AHandlerThatThrowsDoesNotUnsettleTheSessionOrTheOtherHandlers()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        TunerSession session = Session(device, clock, Writer());
        int announced = 0;

        session.Ended += _ => throw new InvalidOperationException("the listener is broken");
        session.Ended += _ => Interlocked.Increment(ref announced);
        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(1, announced);
        Assert.Equal(1, session.FaultCount);
        Assert.True(device.Disposed);
    }

    [Fact]
    public void TheBytesThatWereReadAreTheBytesThatWereWritten()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        RecordingWriter writer = Writer();
        using TunerSession session = Session(device, clock, writer);

        session.Start();
        ReadExactly(device, 4);
        session.Stop();
        WaitForEnd(session);

        long written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.Equal(4, device.Reads);
        Assert.Equal(device.Reads * ChunkSize, written);
        Assert.Equal(written, session.BytesRecorded);
    }

    [Fact]
    public void TheDeviceIsReadOncePerChunk()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        session.Start();
        ReadExactly(device, 4);
        session.Stop();
        WaitForEnd(session);

        long seen = session.Counters.Packets + session.Counters.ProvisionalPackets;

        Assert.Equal(4, device.Reads);
        Assert.Equal(device.Reads * 4L, seen);
    }

    [Fact]
    public void AStalledViewerDoesNotCostTheRecordingAByte()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        RecordingWriter writer = Writer();
        using TunerSession session = Session(device, clock, writer);
        int chunks = SessionBroadcaster.DefaultViewerCapacity + 1;

        SessionSubscription stalled = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        ReadExactly(device, chunks);
        session.Stop();
        WaitForEnd(session);

        long written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.Equal(chunks, device.Reads);
        Assert.Equal(device.Reads * ChunkSize, written);
        Assert.Equal(1, stalled.DroppedChunks);
        Assert.False(stalled.IsDisconnected);
    }

    [Fact]
    public void ARecordingNeverBlocksForASurveyReader()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        RecordingWriter writer = Writer();
        using TunerSession session = Session(device, clock, writer);
        int chunks = SessionBroadcaster.DefaultSurveyCapacity + 1;

        SessionSubscription stalled = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        ReadExactly(device, chunks);

        Assert.True(stalled.IsDisconnected);

        session.Stop();
        WaitForEnd(session);

        long written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.Equal(chunks, device.Reads);
        Assert.Equal(device.Reads * ChunkSize, written);
    }

    [Fact]
    public async Task ASurveyReaderOnARecordingIsRefusedOutrightAndNotAfterAWait()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        SessionSubscription stalled = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        ReadExactly(device, SessionBroadcaster.DefaultSurveyCapacity + 1);
        session.Stop();
        WaitForEnd(session);

        Func<Task> reading = async () =>
        {
            await foreach (byte[] _ in stalled.Reader.ReadAllAsync())
            { }
        };

        Exception refusal = await Record.ExceptionAsync(reading);

        Assert.IsType<IOException>(refusal);
        Assert.True(stalled.IsTruncated);
    }

    [Fact]
    public void ASurveySessionWaitsForItsReaderInsteadOfDroppingTheStream()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, purpose: SessionPurpose.Survey);
        int capacity = SessionBroadcaster.DefaultSurveyCapacity;

        SessionSubscription reader = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        ReadExactly(device, capacity);

        Assert.False(reader.IsDisconnected);
        Assert.Equal(0, reader.DroppedChunks);

        device.Allow(1);

        Assert.True(reader.Reader.TryRead(out _));

        device.AwaitParkedBefore(capacity + 2);

        int taken = 1;
        while (reader.Reader.TryRead(out _))
        {
            taken++;
        }

        Assert.Equal(capacity + 1, taken);
        Assert.False(reader.IsDisconnected);
        Assert.Equal(0, reader.DroppedChunks);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void APiggybackReaderIsDropTolerantLikeAViewer()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());
        int chunks = SessionBroadcaster.DefaultViewerCapacity + 1;

        SessionSubscription stalled = session.Broadcaster.Subscribe(SubscriberKind.Piggyback);

        session.Start();
        ReadExactly(device, chunks);
        session.Stop();
        WaitForEnd(session);

        Assert.False(stalled.IsDisconnected);
        Assert.Equal(1, stalled.DroppedChunks);
    }

    [Fact]
    public async Task AFailedSessionAbortsItsReadersRatherThanClosingCleanly()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(
            new ScriptedTunerDevice(failAfterReads: 3),
            clock,
            Writer()
        );

        SessionSubscription viewer = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        WaitForEnd(session);

        Func<Task> reading = async () =>
        {
            await foreach (byte[] _ in viewer.Reader.ReadAllAsync())
            { }
        };

        await Assert.ThrowsAsync<IOException>(reading);
    }

    [Fact]
    public async Task AStoppedSessionClosesItsReadersCleanly()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        SessionSubscription viewer = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        await foreach (byte[] _ in viewer.Reader.ReadAllAsync())
        { }
    }

    [Fact]
    public async Task AReaderArrivingAfterTheEndIsClosedRatherThanLeftWaiting()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        SessionSubscription late = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        await foreach (byte[] _ in late.Reader.ReadAllAsync())
        { }

        Assert.True(late.IsDisconnected);
        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public void ASubscriberComingAndGoingDoesNotChangeTheSessionState()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        SessionSubscription subscription = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        Assert.Equal(SessionState.Active, session.State);

        session.Broadcaster.Unsubscribe(subscription);

        Assert.Equal(SessionState.Active, session.State);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void MeasurementCarriesOnAcrossReadsWithinOneSession()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        session.Start();
        ReadExactly(device, 8);
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(0, session.Counters.Drops);
        Assert.Equal(8 * 4L, session.Counters.Packets + session.Counters.ProvisionalPackets);
    }

    [Fact]
    public void AnEndTimeMovesForwardAndNeverBack()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        Assert.True(session.Extend(Start.AddHours(3)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);

        Assert.False(session.Extend(Start.AddHours(2)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);
    }

    [Fact]
    public void AnEndedSessionCannotBeExtended()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.False(session.Extend(Start.AddHours(3)));
        Assert.Equal(Start.AddHours(1), session.EndsAt);
    }

    [Fact]
    public void ASessionCannotEndBeforeItBegins()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new TunerSession(
                    SessionId.Parse("s-2"),
                    SessionPurpose.Live,
                    "adapter0",
                    new ScriptedTunerDevice(),
                    Start,
                    Start,
                    new ManualTimeProvider(Start)
                )
        );
    }

    [Fact]
    public void ASessionStartsOnce()
    {
        var clock = new ManualTimeProvider(Start);
        using TunerSession session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();

        Assert.Throws<InvalidOperationException>(session.Start);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void TheDeviceIsReleasedWhenTheSessionEnds()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        Assert.True(device.Disposed);
    }

    [Fact]
    public void ASessionThatNeverStartedStillReleasesWhatItWasGiven()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = new CountingRecordingWriter(Path.Combine(root, "s-1.ts"));

        TunerSession session = Session(device, clock, writer);
        session.Dispose();

        Assert.True(device.Disposed);
        Assert.True(writer.Disposed);
    }

    [Fact]
    public void DisposingASessionThatWillNotStopDoesNotReportACleanStop()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new StubbornTunerDevice(TimeSpan.FromSeconds(7));
        var writer = new CountingRecordingWriter(Path.Combine(root, "s-1.ts"));

        var session = new TunerSession(
            SessionId.Parse("s-1"),
            SessionPurpose.Recording,
            "adapter0",
            device,
            Start,
            Start + TimeSpan.FromHours(1),
            clock,
            writer,
            ChunkSize
        );

        session.Start();

        Assert.True(device.Reading.Wait(TimeSpan.FromSeconds(10)));

        session.Dispose();

        Assert.NotEqual(SessionState.Stopped, session.State);
        Assert.False(session.Completion.IsCompleted);
        Assert.False(device.Disposed);
        Assert.False(writer.Disposed);
        Assert.Equal(1, session.FaultCount);

        session.WaitForEnd(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task ASubscriberLosingAChunkToAStopIsNotToldTheStreamFinished()
    {
        using var broadcaster = new SessionBroadcaster(
            surveyCapacity: 1,
            surveyBlockLimit: TimeSpan.FromSeconds(5)
        );
        SessionSubscription survey = broadcaster.Subscribe(SubscriberKind.Survey);
        using var stopping = new CancellationTokenSource();

        broadcaster.Publish(new byte[] { 1 }, stopping.Token);
        stopping.Cancel();
        broadcaster.Publish(new byte[] { 2 }, stopping.Token);
        broadcaster.Close(null);

        Func<Task> reading = async () =>
        {
            await foreach (byte[] _ in survey.Reader.ReadAllAsync())
            { }
        };

        await Assert.ThrowsAsync<IOException>(reading);
        Assert.True(survey.IsTruncated);
        Assert.Equal(1, survey.DroppedChunks);
    }

    [Fact]
    public async Task ASubscriberThatSawEveryChunkIsToldTheStreamFinished()
    {
        using var broadcaster = new SessionBroadcaster(surveyCapacity: 4);
        SessionSubscription survey = broadcaster.Subscribe(SubscriberKind.Survey);

        broadcaster.Publish(new byte[] { 1 });
        broadcaster.Close(null);

        int taken = 0;
        await foreach (byte[] _ in survey.Reader.ReadAllAsync())
        {
            taken++;
        }

        Assert.Equal(1, taken);
        Assert.False(survey.IsTruncated);
    }

    [Fact]
    public void ASessionWhoseTunerWasTakenIsNeverMistakenForOneThatFinished()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        session.Start();
        ReadExactly(device, 2);
        session.Preempt("a recording took this tuner");
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionStopReason.Preempted, session.StopReason);
        Assert.NotEqual(SessionStopReason.Requested, session.StopReason);
        Assert.NotNull(session.FailureCause);
    }

    [Fact]
    public async Task AReaderWhoseTunerWasTakenIsCutOffAndToldWhatTookIt()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        SessionSubscription viewer = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        ReadExactly(device, 2);
        session.Preempt("a recording of another channel took this tuner");
        WaitForEnd(session);

        int taken = 0;
        Func<Task> reading = async () =>
        {
            await foreach (byte[] _ in viewer.Reader.ReadAllAsync())
            {
                taken++;
            }
        };

        Exception cut = await Record.ExceptionAsync(reading);

        Assert.Equal(2, taken);
        Assert.NotNull(cut);
        Assert.Contains("took this tuner", cut.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionThatWasNotToldWhyStillSaysItsStreamIsIncomplete()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new PacedTunerDevice();
        using TunerSession session = Session(device, clock, Writer());

        session.Start();
        ReadExactly(device, 1);
        session.Stop(SessionStopReason.Preempted);
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Contains("incomplete", session.FailureCause!.Message, StringComparison.Ordinal);
    }

    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(10);

    private static SignalQualityWatch Watch(
        Action<TunerSession, SignalQualitySample>? lockLost = null
    ) => new(WatchInterval, lockLost);

    private sealed class Pacer(PacedTunerDevice device)
    {
        private int taken;

        public void Read(int chunks)
        {
            device.Allow(chunks);
            taken += chunks;
            device.AwaitParkedBefore(taken + 1);
        }
    }

    private static void ReadExactly(PacedTunerDevice device, int chunks)
    {
        device.Allow(chunks);
        device.AwaitParkedBefore(chunks + 1);
    }
}
