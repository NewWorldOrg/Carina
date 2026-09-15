using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingAcrossADriverStopTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan RoomToWatchTheStop = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan PastTheEnd = TimeSpan.FromSeconds(30);

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
}
