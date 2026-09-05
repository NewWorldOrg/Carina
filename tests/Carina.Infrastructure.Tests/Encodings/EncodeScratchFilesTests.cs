using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeScratchFilesTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-ED2-010: the ledger is written before a path to write to is handed out")]
    public async Task TheLedgerIsWrittenBeforeAPathIsHandedOut()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();

        string? path = await harness.ScratchFiles.RecordAsync(job, EncodeScratchKind.WorkFile, job.WorkFileName, Cancel);

        Assert.Equal(Path.Combine(harness.Room.Root, job.WorkFileName.Value), path);
        Assert.False(File.Exists(path), "nothing is created here; only the ledger is written");
        EncodeScratchFile recorded = Assert.Single(harness.Scratch.Files);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(EncodeScratchKind.WorkFile, recorded.Kind);
        Assert.Equal(EncodeHarness.Primary, recorded.OutputRoot);
        Assert.Equal(job.WorkFileName, recorded.FileName);
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, recorded.WrittenAt);
        Assert.True(recorded.IsOwedARemoval);
    }

    [Fact]
    public async Task ThePathIsInTheWorkingDirectoryWhenOneIsNamed()
    {
        using var harness = new EncodeHarness(workingBeside: false);
        EncodeJob job = harness.Running();

        string? path = await harness.ScratchFiles.RecordAsync(job, EncodeScratchKind.Chapters, new EncodeFileName("a.chapters"), Cancel);

        Assert.Equal(Path.Combine(harness.Workshop!.Root, "a.chapters"), path);
    }

    [Fact]
    public async Task ARootThisProcessCannotPlaceGivesNoPathAndWritesNothingDown()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), new OutputRoot("elsewhere"), EncodeHarness.Queued);

        Assert.Null(await harness.ScratchFiles.RecordAsync(job, EncodeScratchKind.WorkFile, job.WorkFileName, Cancel));
        Assert.Empty(harness.Scratch.Files);
    }
}
