using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingAcrossAnAppSwapTests
{
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
}
