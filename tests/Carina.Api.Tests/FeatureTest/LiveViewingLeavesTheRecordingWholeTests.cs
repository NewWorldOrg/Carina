using System.Net;
using System.Runtime.Versioning;

using Carina.Api.Live;
using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class LiveViewingLeavesTheRecordingWholeTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static readonly Uri Watched = new("/api/live/32736-1024/stream", UriKind.Relative);

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

        SessionSnapshot watching = await WatchedSessionAsync(feature);
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
                sessions => LostChunks(Viewing(sessions)) > 0,
                sessions => $"the viewer has lost {LostChunks(Viewing(sessions))} chunk(s)",
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

    [Fact]
    public async Task AViewerWatchingWhatIsBeingRecordedRidesTheSameTunerRatherThanAskingForASecondOne()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(window: Window);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);

        await feature.UntilTheFileGrowsPast(feature.FileOf(started), 0);

        using HttpResponseMessage opened = await feature.App.Client.GetAsync(
            Watched,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        SessionSnapshot watching = await WatchedSessionAsync(feature);
        SessionSnapshot writing = Assert.Single(
            await feature.App.SessionsAsync(),
            session => session.Purpose is SessionPurpose.Recording);

        Assert.Equal(writing.DeviceId, watching.DeviceId);
        Assert.Equal(SessionState.Active, writing.State);
        Assert.Equal(started.Wire, writing.RecordingId);
    }

    private static async Task<SessionSnapshot> WatchedSessionAsync(AppSwapFeature feature)
    {
        IReadOnlyList<SessionSnapshot> held = await Eventually.Yields(
            feature.App.SessionsAsync,
            sessions => Viewing(sessions) is not null,
            sessions => $"the driver holds {sessions.Count} session(s)",
            "the viewer is given a session of its own on the driver");
        SessionSnapshot? watching = Viewing(held);

        Assert.NotNull(watching);

        return watching;
    }

    private static SessionSnapshot? Viewing(IReadOnlyList<SessionSnapshot> sessions)
        => sessions.FirstOrDefault(session => session.Purpose is SessionPurpose.Live);

    private static long LostChunks(SessionSnapshot? session)
        => session?.ViewerLosses.Sum(loss => loss.ChunksDroppedSinceItJoined) ?? 0;
}
