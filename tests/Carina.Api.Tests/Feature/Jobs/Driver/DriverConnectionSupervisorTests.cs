using System.Runtime.Versioning;

using Carina.Api.Tests.Support;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Jobs.Driver;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class DriverConnectionSupervisorTests
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

    [Fact]
    public async Task ARecordingWhoseDriverWentAfterItsBroadcastEndedIsMarkedForWhatWasLeftAndKeepsItsFile()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            takingRecordingsBack: true,
            weighingWhatIsOnTheDisk: true);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        Assert.Equal(started, Assert.Single(feature.Recordings.Rows).Id);

        await feature.UntilTheFileGrowsPast(file, 0);
        await feature.StopAppAsync();
        await feature.RaiseAnotherDriverAsync();

        long whatTheDriverLeft = new FileInfo(file).Length;

        Assert.True(whatTheDriverLeft > 0, "The driver went before a single byte had been written.");

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        await feature.StartAppAsync(takingRecordingsBack: true);

        IReadOnlyList<Recording> rows = await Eventually.Yields(
            () => Task.FromResult(feature.Recordings.Rows),
            held => held.Count is 1 && held[0].Outcome is not null,
            held => string.Join(", ", held.Select(row => row.Outcome?.ToString() ?? "still in flight")),
            "recovery has marked the recording whose broadcast was over by the time it was found");

        Recording marked = Assert.Single(rows);

        Assert.Equal(RecordingOutcome.Truncated, marked.Outcome);
        Assert.False(marked.IsInFlight, "A recording recovery has marked was left in flight.");
        Assert.Contains(marked.OutcomeDetail, detail => detail.Fault is RecordingFault.LeftRunningUnwatched);
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        Assert.Equal(whatTheDriverLeft, new FileInfo(file).Length);
    }
}
