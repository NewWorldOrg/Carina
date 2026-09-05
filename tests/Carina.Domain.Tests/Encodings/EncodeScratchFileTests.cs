using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeScratchFileTests
{
    private static readonly DateTime Written = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Removed = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly RecordingId Recording = RecordingId.New();

    private static readonly EncodeJobId Job = EncodeJobId.New();

    private static EncodeScratchFile Recorded()
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            Job,
            EncodeScratchKind.WorkFile,
            new OutputRoot("primary"),
            EncodeFileName.Working(Recording, Job, 1),
            Written);

    [Fact(DisplayName = "BR-ED2-010: a scratch file is written into the ledger before it exists, and is then still owed a removal")]
    public void AScratchFileIsWrittenIntoTheLedgerBeforeItExistsAndIsOwedARemoval()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Equal(Job, scratch.JobId);
        Assert.Equal(EncodeScratchKind.WorkFile, scratch.Kind);
        Assert.Equal(Written, scratch.WrittenAt);
        Assert.Null(scratch.RemovedAt);
        Assert.Null(scratch.Fate);
        Assert.True(scratch.IsOwedARemoval);
    }

    [Theory]
    [InlineData(EncodeScratchFate.Removed)]
    [InlineData(EncodeScratchFate.AlreadyGone)]
    [InlineData(EncodeScratchFate.BecameTheArtefact)]
    [InlineData(EncodeScratchFate.CouldNotBeRemoved)]
    public void EveryFateSettlesTheRemovalOwed(EncodeScratchFate fate)
    {
        EncodeScratchFile scratch = Recorded();

        scratch.Settle(fate, Removed);

        Assert.Equal(fate, scratch.Fate);
        Assert.Equal(Removed, scratch.RemovedAt);
        Assert.False(scratch.IsOwedARemoval);
    }

    [Fact(DisplayName = "BR-ED2-010: a removal is owed once and settled once")]
    public void ARemovalIsOwedOnceAndSettledOnce()
    {
        EncodeScratchFile scratch = Recorded();
        scratch.Settle(EncodeScratchFate.Removed, Removed);

        Assert.Throws<InvalidOperationException>(() => scratch.Settle(EncodeScratchFate.AlreadyGone, Removed));
    }

    [Fact]
    public void AFateNobodyNamedIsNotAFate()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Throws<ArgumentOutOfRangeException>(() => scratch.Settle((EncodeScratchFate)99, Removed));
    }

    [Fact]
    public void AKindNobodyNamedIsNotAKind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            Job,
            (EncodeScratchKind)99,
            new OutputRoot("primary"),
            EncodeFileName.Working(Recording, Job, 1),
            Written));

    [Fact]
    public void ARemovalCannotBeSettledBeforeTheFileWasWritten()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Throws<ArgumentOutOfRangeException>(() => scratch.Settle(EncodeScratchFate.Removed, Written.AddSeconds(-1)));
    }

    [Fact]
    public void AScratchFileCannotBeMadeWithoutGoingThroughTheOneWayIn()
        => Assert.Empty(typeof(EncodeScratchFile).GetConstructors());
}
