using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class TunerSessionTests : IDisposable
{
    private const int ChunkSize = TsPacketReader.PacketLength * 4;

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("carina-session-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private TunerSession Session(
        ScriptedTunerDevice device,
        ManualTimeProvider clock,
        IRecordingWriter? writer = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TimeSpan? runsFor = null
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
            ChunkSize
        );

    private RecordingWriter Writer(string name = "s-1") => new(root, SessionId.Parse(name));

    private static void WaitForEnd(TunerSession session) =>
        session.WaitForEnd(TimeSpan.FromSeconds(10));

    private static void WaitUntilRecording(TunerSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

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
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

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
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

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
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

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
        using var session = Session(new ScriptedTunerDevice(failAfterReads: 3), clock, Writer());

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
        using var session = Session(new ScriptedTunerDevice(emptyAfterReads: 3), clock, Writer());

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.IsType<EndOfStreamException>(session.FailureCause);
    }

    [Fact]
    public void AWriteFailureEndsTheSessionAsFailedAndNotStopped()
    {
        var clock = new ManualTimeProvider(Start);
        var writer = Writer();
        writer.Dispose();

        using var session = Session(new ScriptedTunerDevice(), clock, writer);

        session.Start();
        WaitForEnd(session);

        Assert.Equal(SessionState.Failed, session.State);
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
        var session = Session(device, clock, writer);

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
        var session = Session(device, clock, writer);
        var announced = 0;

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
        var session = Session(device, clock, Writer());
        var announced = 0;

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
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        session.Start();
        WaitUntilPast(device, 0);
        session.Stop();
        WaitForEnd(session);

        var written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.Equal(device.Reads * ChunkSize, written);
        Assert.Equal(written, session.BytesRecorded);
    }

    [Fact]
    public void TheDeviceIsReadOncePerChunk()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, Writer());

        session.Start();
        WaitUntilPast(device, 0);
        session.Stop();
        WaitForEnd(session);

        var seen = session.Counters.Packets + session.Counters.ProvisionalPackets;

        Assert.True(device.Reads > 0);
        Assert.InRange(seen, (device.Reads - 1) * 4L, device.Reads * 4L);
    }

    [Fact]
    public void AStalledViewerDoesNotCostTheRecordingAByte()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        var stalled = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        WaitUntilPast(device, SessionBroadcaster.DefaultViewerCapacity);
        session.Stop();
        WaitForEnd(session);

        var written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.True(device.Reads > SessionBroadcaster.DefaultViewerCapacity);
        Assert.Equal(device.Reads * ChunkSize, written);
        Assert.True(stalled.DroppedChunks > 0);
        Assert.False(stalled.IsDisconnected);
    }

    [Fact]
    public void ARecordingNeverBlocksForASurveyReader()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        var writer = Writer();
        using var session = Session(device, clock, writer);

        var stalled = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        WaitUntilPast(device, SessionBroadcaster.DefaultSurveyCapacity);
        session.Stop();
        WaitForEnd(session);

        var written = new FileInfo(Path.Combine(root, "s-1.ts")).Length;

        Assert.True(stalled.IsDisconnected);
        Assert.Equal(device.Reads * ChunkSize, written);
    }

    [Fact]
    public void ASurveySessionWaitsForItsReaderInsteadOfDroppingTheStream()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, purpose: SessionPurpose.Survey);

        var reader = session.Broadcaster.Subscribe(SubscriberKind.Survey);

        session.Start();
        WaitUntilPast(device, SessionBroadcaster.DefaultSurveyCapacity);

        Assert.False(reader.IsDisconnected);
        Assert.Equal(0, reader.DroppedChunks);

        var taken = 0;
        while (taken < SessionBroadcaster.DefaultSurveyCapacity && reader.Reader.TryRead(out _))
        {
            taken++;
        }

        Assert.Equal(SessionBroadcaster.DefaultSurveyCapacity, taken);

        session.Stop();
        WaitForEnd(session);
    }

    [Fact]
    public void APiggybackReaderIsDropTolerantLikeAViewer()
    {
        var clock = new ManualTimeProvider(Start);
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, Writer());

        var stalled = session.Broadcaster.Subscribe(SubscriberKind.Piggyback);

        session.Start();
        WaitUntilPast(device, SessionBroadcaster.DefaultViewerCapacity);
        session.Stop();
        WaitForEnd(session);

        Assert.False(stalled.IsDisconnected);
        Assert.True(stalled.DroppedChunks > 0);
    }

    [Fact]
    public async Task AFailedSessionAbortsItsReadersRatherThanClosingCleanly()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(
            new ScriptedTunerDevice(failAfterReads: 3),
            clock,
            Writer()
        );

        var viewer = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        WaitForEnd(session);

        var reading = async () =>
        {
            await foreach (var _ in viewer.Reader.ReadAllAsync())
            { }
        };

        await Assert.ThrowsAsync<IOException>(reading);
    }

    [Fact]
    public async Task AStoppedSessionClosesItsReadersCleanly()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        var viewer = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        session.Start();
        session.Stop();
        WaitForEnd(session);

        await foreach (var _ in viewer.Reader.ReadAllAsync())
        { }
    }

    [Fact]
    public async Task AReaderArrivingAfterTheEndIsClosedRatherThanLeftWaiting()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        session.Stop();
        WaitForEnd(session);

        var late = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

        await foreach (var _ in late.Reader.ReadAllAsync())
        { }

        Assert.True(late.IsDisconnected);
        Assert.Equal(0, session.Broadcaster.SubscriberCount);
    }

    [Fact]
    public void ASubscriberComingAndGoingDoesNotChangeTheSessionState()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        session.Start();
        var subscription = session.Broadcaster.Subscribe(SubscriberKind.Viewer);

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
        var device = new ScriptedTunerDevice();
        using var session = Session(device, clock, Writer());

        session.Start();
        WaitUntilPast(device, 8);
        session.Stop();
        WaitForEnd(session);

        Assert.Equal(0, session.Counters.Drops);
        Assert.True(session.Counters.Packets > 4);
    }

    [Fact]
    public void AnEndTimeMovesForwardAndNeverBack()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

        Assert.True(session.Extend(Start.AddHours(3)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);

        Assert.False(session.Extend(Start.AddHours(2)));
        Assert.Equal(Start.AddHours(3), session.EndsAt);
    }

    [Fact]
    public void AnEndedSessionCannotBeExtended()
    {
        var clock = new ManualTimeProvider(Start);
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

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
        using var session = Session(new ScriptedTunerDevice(), clock, Writer());

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
        using var session = Session(device, clock, Writer());

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

        var session = Session(device, clock, writer);
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
        var survey = broadcaster.Subscribe(SubscriberKind.Survey);
        using var stopping = new CancellationTokenSource();

        broadcaster.Publish(new byte[] { 1 }, stopping.Token);
        stopping.Cancel();
        broadcaster.Publish(new byte[] { 2 }, stopping.Token);
        broadcaster.Close(null);

        var reading = async () =>
        {
            await foreach (var _ in survey.Reader.ReadAllAsync())
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
        var survey = broadcaster.Subscribe(SubscriberKind.Survey);

        broadcaster.Publish(new byte[] { 1 });
        broadcaster.Close(null);

        var taken = 0;
        await foreach (var _ in survey.Reader.ReadAllAsync())
        {
            taken++;
        }

        Assert.Equal(1, taken);
        Assert.False(survey.IsTruncated);
    }

    private static void WaitUntilPast(ScriptedTunerDevice device, long reads)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (device.Reads <= reads && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        Assert.True(device.Reads > reads);
    }
}
