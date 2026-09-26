using System.Runtime.Versioning;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class RecordingMarkedAfterTheDriverWentTests
{
    /// <summary>
    /// Acceptance bar 8, the third of the three branches: nothing is writing the recording and the
    /// broadcast is over by the time this side comes back. The recording is marked for what was
    /// left of it, names why nothing was writing it, and keeps its file. An app that has only just
    /// started has never seen the driver before, so what it can say is that the recording was left
    /// running unwatched rather than that the driver was replaced. The other two branches are held
    /// by RecordingAcrossAnAppSwapTests and RecordingTakenBackAfterTheDriverWentTests; none of the
    /// three may reach complete.
    /// </summary>
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
