extern alias driver;

using System.Runtime.Versioning;

using Carina.Api.Tests.Support;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Descrambling;
using driver::Puppis.Driver.Recording;
using driver::Puppis.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Jobs.Recordings;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class RecordingStreamJobTests
{
    private static readonly RecordingWatchSettings AtOnce = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(20),
        1,
        TimeSpan.FromMilliseconds(20),
        3);

    [Fact]
    public async Task TheCountsARecordingReachedAreStillOnItAfterTheAppWentAndCameBack()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        await feature.UntilTheFileGrowsPast(file, SyntheticDriverHost.AChunkOrThree);

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

    [Fact]
    public async Task AStreamThatEndsItselfIsNeverCountedAsARecordingThatFinished()
    {
        var tuners = new StreamThatEndsItselfOnce(SyntheticDriverHost.AChunkOrThree);

        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.AddSingleton<ITunerDeviceFactory>(tuners),
            watching: AtOnce);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        await feature.UntilTheFileGrowsPast(file, 0);

        IReadOnlyList<SessionSnapshot> ended = await Eventually.Yields(
            feature.App.SessionsAsync,
            held => held.Any(one => one.State is SessionState.Failed),
            held => string.Join(", ", held.Select(one => one.State.ToString())),
            "the driver has ended the session whose stream stopped giving bytes");

        SessionSnapshot cut = Assert.Single(ended);

        Assert.Equal(SessionState.Failed, cut.State);
        Assert.NotEqual(SessionStopReason.EndTimeReached, cut.StopReason);

        long whenTheStreamEnded = new FileInfo(file).Length;

        Assert.True(whenTheStreamEnded > 0, "The stream ended before a single byte had been written.");

        feature.Clock.Turn(TimeSpan.FromSeconds(30));

        RecordingStreamSupervisor supervisor = feature.App.Services.GetRequiredService<RecordingStreamSupervisor>();

        using var patience = new CancellationTokenSource(Eventually.Patience);

        RecordingWatch mended = await supervisor.WatchAsync(patience.Token);

        Assert.Equal(1, mended.Broken);
        Assert.Equal(0, mended.Settled);
        Assert.True(
            mended.Resumed + mended.LeftOpen is 1,
            $"The stream was neither put back nor left open for the next pass: resumed {mended.Resumed}, left open {mended.LeftOpen}.");

        Recording carried = Assert.Single(feature.Recordings.Rows);

        Assert.Null(carried.Outcome);
        Assert.True(
            carried.IsInFlight,
            "A recording whose stream ended on its own was given an outcome instead of being carried on.");

        Interruption ofTheEnd = Assert.Single(carried.Interruptions);

        Assert.Equal(RecordingFault.DriverLost, ofTheEnd.Fault);

        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        Assert.True(
            new FileInfo(file).Length >= whenTheStreamEnded,
            "The bytes the recording had written were lost when its stream ended.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADiskThatFillsUpFailsTheRecordingWithItsClassAndOpensNothingAgain(bool weighed)
    {
        var disk = new DiskThatFillsUp(SyntheticDriverHost.AChunkOrThree);

        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.AddSingleton<IRecordingWriterFactory>(disk),
            weighingWhatIsOnTheDisk: weighed);

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        IReadOnlyList<SessionSnapshot> ended = await Eventually.Yields(
            feature.App.SessionsAsync,
            held => held.Any(one => one.State is SessionState.Failed),
            held => string.Join(", ", held.Select(one => one.State.ToString())),
            "the driver has failed the session whose disk filled up");

        SessionSnapshot starved = Assert.Single(ended);

        Assert.Equal(SessionStopReason.RecordingFailed, starved.StopReason);
        Assert.Equal(SessionRefusalTitles.DiskFull, starved.FailureTitle);

        RecordingStreamSupervisor supervisor = feature.App.Services.GetRequiredService<RecordingStreamSupervisor>();

        using var patience = new CancellationTokenSource(Eventually.Patience);

        RecordingWatch first = await supervisor.WatchAsync(patience.Token);
        RecordingWatch second = await supervisor.WatchAsync(patience.Token);
        RecordingRun later = await feature.App.TickAsync();

        Recording failed = Assert.Single(feature.Recordings.Rows);

        Assert.Equal(1, first.Settled);
        Assert.Equal(0, first.Broken);
        Assert.Equal(0, second.Watched);
        Assert.Equal(RecordingOutcome.Failed, failed.Outcome);
        Assert.Equal(
            weighed
                ? [RecordingFault.DiskExhausted]
                : [RecordingFault.DiskExhausted, RecordingFault.SizeUnobserved],
            failed.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Equal(weighed ? new FileInfo(file).Length : 0, failed.FileSizeObserved);
        Assert.Empty(later.Started);
        Assert.Equal(1, disk.Opened);
        Assert.Single(await feature.App.SessionsAsync());
        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
        Assert.InRange(new FileInfo(file).Length, 1, SyntheticDriverHost.AChunkOrThree);

        IDriverClient client = feature.App.Services.GetRequiredService<IDriverClient>();
        DriverCall<IReadOnlyList<DiagnosticSnapshot>> diagnosed = await client.GetDiagnosticsAsync(CancellationToken.None);

        Assert.True(diagnosed.TryGetValue(out IReadOnlyList<DiagnosticSnapshot>? entries));
        Assert.Contains(
            entries,
            entry => entry.Reason is DiagnosticReason.RecordingWriteFailed && entry.SessionId.Equals(starved.SessionId));
        Assert.True((await client.GetHealthAsync(CancellationToken.None)).TryGetValue(out DriverHello? _));
    }
}
