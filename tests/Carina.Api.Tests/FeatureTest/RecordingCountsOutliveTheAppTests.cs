using System.Runtime.Versioning;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingCountsOutliveTheAppTests
{
    /// <summary>
    /// Acceptance bar 7: the counts a recording had reached are on the ledger while it is still
    /// being written, and the app going away and coming back does not put them back to nothing.
    /// The regression this holds off is the one the current system has, where a recording that died
    /// part way through is indistinguishable from a perfect one because both say zero.
    /// </summary>
    [Fact]
    public async Task TheCountsARecordingReachedAreStillOnItAfterTheAppWentAndCameBack()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        await feature.UntilTheFileGrowsPast(file, SyntheticDriverHost.AChunkOrThree);

        // A pass writes nothing down for an instant that is not past the one the recording was last
        // counted at, and this clock stands still until a test turns it.
        feature.Clock.Turn(TimeSpan.FromSeconds(30));

        using var patience = new CancellationTokenSource(Eventually.Patience);

        await feature.App.Services
            .GetRequiredService<RecordingStreamSupervisor>()
            .WatchAsync(patience.Token);

        Recording counted = Assert.Single(feature.Recordings.Rows);

        Assert.True(counted.CcMeasured, "The driver's counts had not reached the ledger before the app went.");
        Assert.True(counted.CcTotalPackets is > 0, "The recording was counted as carrying no packet at all.");

        long reachedBeforeTheSwap = counted.CcTotalPackets ?? 0;

        await feature.StopAppAsync();
        await feature.StartAppAsync();
        await feature.App.UntilReadoptions(1);

        Recording readopted = Assert.Single(feature.Recordings.Rows);

        Assert.True(readopted.CcMeasured, "The recording came back from the swap saying nothing had been counted.");
        Assert.True(
            readopted.CcTotalPackets >= reachedBeforeTheSwap,
            $"The counts went backwards across the swap, from {reachedBeforeTheSwap} to {readopted.CcTotalPackets}.");

        feature.Clock.Turn(TimeSpan.FromSeconds(30));

        using var more = new CancellationTokenSource(Eventually.Patience);

        await feature.App.Services
            .GetRequiredService<RecordingStreamSupervisor>()
            .WatchAsync(more.Token);

        Recording kept = Assert.Single(feature.Recordings.Rows);

        Assert.True(kept.CcMeasured, "A pass by the app that came back wrote the counts down as never taken.");
        Assert.True(
            kept.CcTotalPackets >= reachedBeforeTheSwap,
            $"A pass by the app that came back put the counts back, from {reachedBeforeTheSwap} to {kept.CcTotalPackets}.");
        Assert.Null(kept.Outcome);
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
    }
}
