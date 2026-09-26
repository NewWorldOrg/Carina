extern alias driver;

using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Carina.Driver.Configuration;
using driver::Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// A device that hands over some bytes and then returns none at all, which is how a stream ending
/// cleanly reaches this side: nothing failed, nothing was thrown, the bytes simply stopped.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class EndsCleanlyAfter(ITunerDevice carrying, long after) : ITunerDevice
{
    private long given;

    public long Overflows => carrying.Overflows;

    public ISignalQualitySource? Quality => carrying.Quality;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        if (Interlocked.Read(ref given) >= after)
        {
            return [];
        }

        byte[] chunk = carrying.Read(count, cancellationToken);

        Interlocked.Add(ref given, chunk.Length);

        return chunk;
    }

    public void Dispose() => carrying.Dispose();
}

/// <summary>
/// The first device handed out ends its stream on its own once it has given what it was told to.
/// Every device after it streams on, so nothing about the tuner stands in the way of the stream
/// being opened again and what the test reads is what this side made of the end.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class StreamThatEndsItselfOnce(long after) : ITunerDeviceFactory
{
    private static readonly TimeSpan BetweenReads = TimeSpan.FromMilliseconds(25);

    private int handedOut;

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
    {
        TuningRequest asked = tune?.ToLegacyRequest() ?? tuning;
        var paced = new PacedTunerDevice(
            new FakeTunerDevice(asked.PhysicalChannel, asked.ServiceId),
            BetweenReads);

        return Interlocked.Increment(ref handedOut) is 1
            ? new EndsCleanlyAfter(paced, after)
            : paced;
    }
}

[SupportedOSPlatform("linux")]
public sealed class RecordingAcrossACleanEndOfStreamTests
{
    /// <summary>
    /// The watch reaches for the stream once and waits no time at all between attempts. The pause
    /// the default keeps is served by the hand-turned clock, which rings only when a test turns it,
    /// so a test that let the watch reach one would wait for a pause that never ends.
    /// </summary>
    private static readonly RecordingWatchSettings AtOnce = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(20),
        1,
        TimeSpan.FromMilliseconds(20),
        3);

    /// <summary>
    /// Acceptance bar 1: a stream that ends itself is never a recording that finished. The driver
    /// ends the session as a failure rather than as the end time being reached; the ledger keeps the
    /// recording in flight with no outcome at all and writes the break down with the reason it was;
    /// and this side reaches for the stream again rather than letting the recording stand as done.
    /// The file the recording already has is kept and no second one is opened beside it.
    /// </summary>
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

        // A pass judges nothing at an instant that is not past the one it last read, and this clock
        // stands still until a test turns it. The window is ten minutes, so the recording is not over.
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
}
