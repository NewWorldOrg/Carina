using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingTakenBackAfterTheDriverWentTests
{
    [Fact]
    public async Task ARecordingWhoseDriverWentWhileTheAppWasAwayCarriesOnIntoTheFileItAlreadyHas()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(takingRecordingsBack: true);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        SessionId session = RecordingSessions.Named(started);
        string file = feature.FileOf(started);

        Assert.Equal(session, Assert.Single(await feature.App.SessionsAsync()).SessionId);

        await feature.UntilTheFileGrowsPast(file, 0);
        await feature.StopAppAsync();
        await feature.RaiseAnotherDriverAsync();

        long whatTheDriverLeft = new FileInfo(file).Length;

        await feature.StartAppAsync(takingRecordingsBack: true);
        await feature.UntilTheFileGrowsPast(file, whatTheDriverLeft + SyntheticDriverHost.AChunkOrThree);

        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        Assert.Equal(session, Assert.Single(await feature.App.SessionsAsync()).SessionId);

        Recording carried = Assert.Single(feature.Recordings.Rows);

        Assert.Equal(started, carried.Id);
        Assert.True(carried.IsInFlight, "A recording put back on a stream was given an outcome instead.");
        Assert.Null(carried.Outcome);
        Assert.Equal(1, carried.ResumeCount);

        Interruption ofTheBreak = Assert.Single(carried.Interruptions);

        Assert.Equal(RecordingFault.LeftRunningUnwatched, ofTheBreak.Fault);
        Assert.False(ofTheBreak.IsOpen);

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun overRun = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(overRun.Stopped));

        await feature.UntilTheRecordingSessionIsStopped();

        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        Assert.True(
            new FileInfo(file).Length > whatTheDriverLeft,
            "The file the recording was carried on into is no bigger than what the driver that went left.");
    }
}
