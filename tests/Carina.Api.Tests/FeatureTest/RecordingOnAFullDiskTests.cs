extern alias driver;

using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Carina.Driver.Recording;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
internal sealed class DiskThatFillsUp(long room) : IRecordingWriterFactory
{
    private int opened;

    public int Opened => Volatile.Read(ref opened);

    public IRecordingWriter Open(string recordingsDirectory, string recordingId)
    {
        Interlocked.Increment(ref opened);

        return new WriterOnAFillingDisk(new RecordingWriter(recordingsDirectory, recordingId), room);
    }
}

[SupportedOSPlatform("linux")]
internal sealed class WriterOnAFillingDisk(IRecordingWriter inner, long room) : IRecordingWriter
{
    public string Path => inner.Path;

    public long BytesWritten => inner.BytesWritten;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (inner.BytesWritten + bytes.Length <= room)
        {
            inner.Write(bytes);

            return;
        }

        using var full = new FileStream("/dev/full", FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 0);

        full.Write(bytes);
    }

    public void Dispose() => inner.Dispose();
}

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingOnAFullDiskTests
{
    [Fact]
    public async Task ADiskThatFillsUpFailsTheRecordingWithItsClassAndOpensNothingAgain()
    {
        var disk = new DiskThatFillsUp(SyntheticDriverHost.AChunkOrThree);

        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.AddSingleton<IRecordingWriterFactory>(disk));

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
        Assert.Equal(RecordingFault.DiskExhausted, Assert.Single(failed.OutcomeDetail).Fault);
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
