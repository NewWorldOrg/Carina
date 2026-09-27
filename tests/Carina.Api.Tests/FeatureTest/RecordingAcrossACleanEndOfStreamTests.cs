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
/// A device that hands over some bytes and then returns none at all: a stream ending cleanly.
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
/// Hands out a first device that ends its stream on its own once it has given what it was told to;
/// every device after it streams on.
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
    /// The watch reaches for the stream once and waits no time at all between attempts.
    /// </summary>
    private static readonly RecordingWatchSettings AtOnce = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(20),
        1,
        TimeSpan.FromMilliseconds(20),
        3);

    /// <summary>
    /// A stream that ends itself is never a recording that finished. The driver ends the session as a
    /// failure; the ledger keeps the recording in flight with no outcome and writes the break down with
    /// its reason; this side reaches for the stream again; and the recording keeps its one file.
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
