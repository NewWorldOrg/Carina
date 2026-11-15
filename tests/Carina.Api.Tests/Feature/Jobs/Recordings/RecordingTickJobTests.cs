extern alias driver;

using System.Net;
using System.Runtime.Versioning;

using Carina.Api.Live;
using Carina.Api.Tests.Support;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Jobs.Recordings;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class RecordingTickJobTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan RoomToWatchTheStop = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan PastTheEnd = TimeSpan.FromSeconds(30);

    private static readonly Uri Watched = new("/api/live/32736-1024/stream", UriKind.Relative);

    [Fact]
    public async Task ARecordingOutlivesTheAppThatStartedIt()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        SessionId session = RecordingSessions.Named(started);
        string file = feature.FileOf(started);

        Assert.Equal(session, Assert.Single(await feature.App.SessionsAsync()).SessionId);

        await feature.UntilTheFileGrowsPast(file, 0);

        IServiceProvider departing = feature.App.Services;

        await feature.StopAppAsync();

        Assert.Throws<ObjectDisposedException>(() => departing.GetRequiredService<IDriverClient>());

        long withNoAppAtAll = new FileInfo(file).Length;

        await feature.UntilTheFileGrowsPast(file, withNoAppAtAll + SyntheticDriverHost.AChunkOrThree);

        await feature.StartAppAsync();
        await feature.App.UntilReadoptions(1);

        SessionSnapshot readopted = Assert.Single(feature.App.Readoptions.LastSessions!);

        Assert.Equal(session, readopted.SessionId);
        Assert.Equal(started.Wire, readopted.RecordingId);
        Assert.Equal(SessionState.Active, readopted.State);

        Recording carried = Assert.Single(feature.Recordings.Rows);

        Assert.Equal(started, carried.Id);
        Assert.True(carried.IsInFlight, "The recording the driver is still writing was not left in flight.");

        RecordingRun afterTheSwap = await feature.App.TickAsync();

        Assert.Empty(afterTheSwap.Started);
        Assert.Empty(afterTheSwap.Stopped);
        Assert.Empty(afterTheSwap.Refused);
        Assert.Equal(session, Assert.Single(await feature.App.SessionsAsync()).SessionId);
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun overRun = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(overRun.Stopped));

        await feature.UntilTheRecordingSessionIsStopped();

        SessionCounters counted = SyntheticDriverHost.ContinuityOf(file);

        Assert.True(counted.Packets > 0, "The recording the driver wrote holds no transport stream packet.");
        Assert.Equal(0, counted.Drops);
        Assert.Equal(0, counted.Discontinuities);
        Assert.Equal(0, counted.TransportErrors);
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
    }

    [Fact]
    public async Task ADriverAskedToStopMidRecordingStaysUpUntilTheRecordingReachesItsOwnEnd()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(window: Window);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        SessionId session = RecordingSessions.Named(started);
        string file = feature.FileOf(started);
        SessionSnapshot running = Assert.Single(await feature.App.SessionsAsync());
        DateTimeOffset endsAt = Assert.NotNull(running.EndsAt);

        await feature.UntilTheFileGrowsPast(file, 0);

        TimeSpan left = endsAt - DateTimeOffset.UtcNow;

        Assert.True(
            left >= RoomToWatchTheStop,
            $"Only {left} was left of the recording when the stop was about to be asked for, which is too little to see the driver wait.");

        long whenTheStopWasAsked = new FileInfo(file).Length;
        Task stopping = feature.Driver.BeginStop();

        await feature.App.UntilConnectionIs("draining");
        await feature.UntilTheFileGrowsPast(file, whenTheStopWasAsked + SyntheticDriverHost.AChunkOrThree);

        bool stillStopping = !stopping.IsCompleted;
        DateTimeOffset seenGrowing = DateTimeOffset.UtcNow;

        Assert.True(stillStopping, "The driver finished stopping while the recording it was asked to wait for was still running.");
        Assert.True(seenGrowing < endsAt, "The file was only seen growing once the recording was already over.");

        await stopping.WaitAsync(endsAt - DateTimeOffset.UtcNow + PastTheEnd);

        Assert.True(
            DateTimeOffset.UtcNow >= endsAt,
            $"The driver finished stopping before {endsAt:O}, the end the recording was started with.");

        HeldSession ended = feature.Driver.Held(session);

        Assert.Equal(SessionState.Stopped, ended.State);
        Assert.Equal(SessionStopReason.EndTimeReached, ended.StopReason);
        Assert.Equal(new FileInfo(file).Length, ended.BytesRecorded);

        SessionCounters counted = SyntheticDriverHost.ContinuityOf(file);

        Assert.True(counted.Packets > 0, "The recording the driver wrote holds no transport stream packet.");
        Assert.Equal(0, counted.Drops);
        Assert.Equal(0, counted.Discontinuities);
        Assert.Equal(0, counted.TransportErrors);
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());

        await feature.App.UntilConnectionIs("notConnected");
    }

    [Fact]
    public async Task ARecordingAndItsTunerBothFollowAProgrammeThatRunsLate()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        RecordingId started = Assert.Single((await feature.App.TickAsync()).Started);
        Recording running = Assert.Single(feature.Recordings.Rows);
        DateTime promised = running.ExpectedWindowEnd;
        DateTime runsLate = promised.AddMinutes(5);

        Assert.Equal(promised, Assert.Single(await feature.App.SessionsAsync()).EndsAt?.UtcDateTime);

        Announce(feature, running, runsLate);

        RecordingRun followed = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(followed.Followed).Id);
        Assert.False(Assert.Single(followed.Followed).EndUndecided);
        Assert.Equal(runsLate, Assert.Single(feature.Recordings.Rows).ExpectedWindowEnd);
        Assert.Equal(runsLate, Assert.Single(await feature.App.SessionsAsync()).EndsAt?.UtcDateTime);

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun pastTheOldWindow = await feature.App.TickAsync();

        Assert.Empty(pastTheOldWindow.Stopped);
        Assert.Empty(pastTheOldWindow.Followed);
        Assert.True(Assert.Single(feature.Recordings.Rows).IsInFlight);

        feature.Clock.Turn(TimeSpan.FromMinutes(5));

        Assert.Equal(started, Assert.Single((await feature.App.TickAsync()).Stopped));

        await feature.UntilTheRecordingSessionIsStopped();
    }

    [Fact]
    public async Task ARewrittenLedgerAndARestartMoveWhereTheDriverListensAndWritesWithNothingRebuilt()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        SyntheticDriverHost driver = feature.Driver;
        string firstSocket = driver.SocketPath;
        string firstRecordings = driver.RecordingsDirectory;
        string movedSocket = driver.Beside("moved.sock");
        string movedRecordings = driver.Beside("moved-recordings");
        DriverConfiguration moved = driver.Configuration with
        {
            SocketPath = movedSocket,
            OutputRoots = [new OutputRootSettings(SyntheticDriverHost.RootName, movedRecordings)],
        };

        await driver.PutDownAsync();
        await feature.App.UntilConnectionIs("notConnected");

        driver.WriteLedger(moved);

        using var refusal = new StringWriter();

        Assert.Equal(DriverStartup.ConfigurationExitCode, await driver.RaiseFromTheLedgerAsync(refusal));
        Assert.Contains($"outputRoots[0].path: '{movedRecordings}' does not exist.", refusal.ToString(), StringComparison.Ordinal);
        Assert.Contains(driver.LedgerPath, refusal.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(movedSocket), "A driver whose ledger was refused bound its socket anyway.");
        Assert.False(File.Exists(firstSocket), "A driver whose ledger was refused is still answering where it used to.");

        await feature.App.UntilConnectionIs("notConnected");

        Directory.CreateDirectory(movedRecordings);

        using var acceptance = new StringWriter();

        Assert.Equal(0, await driver.RaiseFromTheLedgerAsync(acceptance));
        Assert.Empty(acceptance.ToString());
        Assert.Equal(movedSocket, driver.SocketPath);

        await feature.StartAppAsync();

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        Assert.Equal(movedRecordings, Path.GetDirectoryName(file));

        await feature.UntilTheFileGrowsPast(file, 0);

        Assert.Empty(Directory.EnumerateFileSystemEntries(firstRecordings));

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun overRun = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(overRun.Stopped));

        await feature.UntilTheRecordingSessionIsStopped();

        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
    }

    [Fact]
    public async Task AViewerThatNeverReadsLosesItsOwnChunksAndTheRecordingKeepsEveryPacketItWrote()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(window: Window);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        SessionId writing = RecordingSessions.Named(started);
        string file = feature.FileOf(started);

        await feature.UntilTheFileGrowsPast(file, 0);

        using HttpResponseMessage opened = await feature.App.Client.GetAsync(
            Watched,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, opened.Content.Headers.ContentType?.MediaType);

        SessionSnapshot watching = await ViewerSessions.WatchedSessionAsync(feature);
        DriverCall<Stream> handed = await feature.App.Services
            .GetRequiredService<IDriverClient>()
            .OpenSessionStreamAsync(watching.SessionId, DriverEndpoints.ViewerSubscriber, CancellationToken.None);

        Assert.True(
            handed.TryGetValue(out Stream? unread),
            $"The driver would not hand a viewer the stream of the channel being recorded: {handed.Outcome}.");

        await using (unread)
        {
            await Eventually.Yields(
                feature.App.SessionsAsync,
                sessions => LostChunks(ViewerSessions.Viewing(sessions)) > 0,
                sessions => $"the viewer has lost {LostChunks(ViewerSessions.Viewing(sessions))} chunk(s)",
                "the viewer that never reads falls behind and the driver throws its chunks away");

            long whenTheViewerFellBehind = new FileInfo(file).Length;

            await feature.UntilTheFileGrowsPast(file, whenTheViewerFellBehind + SyntheticDriverHost.AChunkOrThree);

            SessionCounters counted = SyntheticDriverHost.ContinuityOf(file);
            HeldSession recording = feature.Driver.Held(writing);

            Assert.Equal(SessionState.Active, recording.State);
            Assert.True(counted.Packets > 0, "The recording the driver wrote holds no transport stream packet.");
            Assert.Equal(0, counted.Drops);
            Assert.Equal(0, counted.Discontinuities);
            Assert.Equal(0, counted.TransportErrors);
            Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        }
    }

    private static void Announce(AppSwapFeature feature, Recording running, DateTime? endsAt)
        => feature.Programmes.Programmes.Add(Programme.Discover(
            new ProgrammeBroadcast(
                running.Programme.Id,
                new TransportStreamId(32736),
                running.ProgrammeStartsAt,
                endsAt,
                "A programme that runs late",
                "What it is about",
                false)
            {
                Source = ProgrammeSource.PresentFollowing,
            },
            running.ProgrammeStartsAt));

    private static long LostChunks(SessionSnapshot? session)
        => session?.ViewerLosses.Sum(loss => loss.ChunksDroppedSinceItJoined) ?? 0;
}
