using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Integrity;

public sealed class StrayFileDisposalTests
{
    private const string StrayPath = "nested/stray.bin";

    private static readonly DateTime Noticed = new(2026, 8, 26, 5, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Written = new(2026, 8, 25, 23, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly RecordingFileName Name = new("one.m2ts");

    private static readonly IntegrityCheckId Check = IntegrityCheckId.New();

    private static readonly RecordingId Recording = new(new Guid("5c1d0e2a-0000-0000-0000-000000000007"));

    [Fact]
    public void AFileNoRecordingOwnsWhoseLastWriteWasKeptIsOneToThrowAway()
    {
        Assert.Null(StrayFileDisposal.Refusal(Stray()));
    }

    [Fact]
    public void AFindingAboutARecordingsOwnFileSendsYouToTheRecordingInstead()
    {
        Assert.Equal(
            [StrayFileRefusal.NamesARecording, StrayFileRefusal.NamesARecording, StrayFileRefusal.NamesARecording],
            new[]
            {
                IntegrityFinding.SizeDisagrees(Check, Primary, Recording, Name, 100, 99, Noticed),
                IntegrityFinding.FileEmpty(Check, Primary, Recording, Name, 100, 0, Noticed),
                IntegrityFinding.EmptyThoughComplete(Check, Primary, Recording, Name, 100, 0, Noticed),
            }.Select(StrayFileDisposal.Refusal).ToArray());
    }

    [Fact]
    public void ARowWhoseFileIsMissingHasNothingOnTheDiskToThrowAway()
    {
        Assert.Equal(
            StrayFileRefusal.NothingOnTheDisk,
            StrayFileDisposal.Refusal(IntegrityFinding.FileMissing(Check, Primary, Recording, Name, 100, Noticed)));
    }

    [Fact]
    public void EveryClassTheSweepCanNameIsJudgedAndOnlyTheFileNoRecordingOwnsIsLetThrough()
    {
        Assert.Equal([IntegrityFault.NoLedgerRow], IntegrityFaults.ThatNameAFileNoRecordingOwns);
        Assert.Equal(
            Enum.GetValues<IntegrityFault>().Length,
            IntegrityFaults.ThatNameARecording.Count + IntegrityFaults.ThatNameAFileNoRecordingOwns.Count);
    }

    [Fact]
    public void AFindingKeptWithoutTheTimeOfItsLastWriteIsNotOneToThrowAway()
    {
        IntegrityFinding unstamped = IntegrityFinding.NoLedgerRow(Check, Primary, StrayPath, 512, Noticed);

        Assert.Equal(StrayFileRefusal.NoTimeWasTaken, StrayFileDisposal.Refusal(unstamped));
        Assert.Throws<InvalidOperationException>(() => unstamped.ThrowAway(Noticed.AddMinutes(1)));
    }

    [Fact]
    public void AFileThrownAwayIsNotThrownAwayAgain()
    {
        IntegrityFinding finding = Stray();

        finding.ThrowAway(Noticed.AddMinutes(1));

        Assert.Equal(Noticed.AddMinutes(1), finding.ThrownAwayAt);
        Assert.Equal(StrayFileRefusal.AlreadyThrownAway, StrayFileDisposal.Refusal(finding));
        Assert.Throws<InvalidOperationException>(() => finding.ThrowAway(Noticed.AddMinutes(2)));
    }

    [Fact]
    public void AFileIsNotThrownAwayBeforeItWasFound()
    {
        IntegrityFinding finding = Stray();

        Assert.Throws<ArgumentException>(() => finding.ThrowAway(Noticed.AddSeconds(-1)));
        Assert.Null(finding.ThrownAwayAt);
    }

    [Fact]
    public void AFileTheLedgerNowHoldsARowForIsClaimed()
    {
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(Check, Primary, "late.m2ts", 512, Noticed, Written);

        Assert.True(StrayFileDisposal.Claimed(
            finding,
            [LedgerFile.StillWriting(Recording, Primary, new RecordingFileName("late.m2ts"))],
            []));
        Assert.False(StrayFileDisposal.Claimed(
            finding,
            [LedgerFile.StillWriting(Recording, new OutputRoot("bulk"), new RecordingFileName("late.m2ts"))],
            []));
    }

    [Fact]
    public void AFileEncodeWorkInHandNowDeclaresIsClaimed()
    {
        IntegrityFinding finding = Stray();

        Assert.True(StrayFileDisposal.Claimed(finding, [], [new DeclaredFile(Primary, StrayPath)]));
        Assert.False(StrayFileDisposal.Claimed(finding, [], [new DeclaredFile(Primary, "nested/other.bin")]));
    }

    [Fact]
    public void AFileStillAsItWasFoundHasNotChanged()
    {
        Assert.Null(StrayFileDisposal.Change(Stray(), new StoredFile(StrayPath, 512, Written)));
    }

    [Fact]
    public void AFileNoLongerThereIsGone()
    {
        Assert.Equal(StrayFileChange.Gone, StrayFileDisposal.Change(Stray(), null));
    }

    [Fact]
    public void AFileOfAnotherSizeIsNotTheOneThatWasFound()
    {
        Assert.Equal(StrayFileChange.Resized, StrayFileDisposal.Change(Stray(), new StoredFile(StrayPath, 513, Written)));
    }

    [Fact]
    public void AFileOfTheSameSizeWrittenToSinceIsNotTheOneThatWasFound()
    {
        Assert.Equal(
            StrayFileChange.Rewritten,
            StrayFileDisposal.Change(Stray(), new StoredFile(StrayPath, 512, Written.AddSeconds(1))));
    }

    [Fact]
    public void ALastWriteIsKeptToTheMicrosecondTheStoreHoldsSoAReadBackRowStillMatchesTheDisk()
    {
        IntegrityFinding finding = Stray(Written.AddTicks(7));

        Assert.Equal(Written, finding.LastWrittenAt);
        Assert.Equal(Written, new StoredFile(StrayPath, 512, Written.AddTicks(3)).LastWrittenAt);
        Assert.Null(StrayFileDisposal.Change(finding, new StoredFile(StrayPath, 512, Written.AddTicks(9))));
    }

    [Fact]
    public void OnlyAFileNoRecordingOwnsKeepsALastWriteOrATimeItWasThrownAway()
    {
        IntegrityFindingId id = IntegrityFindingId.Of(IntegrityFault.SizeDisagrees, Primary, Name.Value, Recording);

        Assert.Throws<ArgumentException>(() => IntegrityFinding.Rehydrate(
            id,
            Check,
            IntegrityFault.SizeDisagrees,
            Primary,
            Name.Value,
            Recording,
            100,
            99,
            Noticed,
            Written));
        Assert.Throws<ArgumentException>(() => IntegrityFinding.Rehydrate(
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, StrayPath, null),
            Check,
            IntegrityFault.NoLedgerRow,
            Primary,
            StrayPath,
            null,
            null,
            512,
            Noticed,
            null,
            Noticed.AddMinutes(1)));
    }

    [Fact]
    public void AnErasureSaysHowTheFileChangedOnlyWhenThatIsWhyItWasRefused()
    {
        Assert.Throws<ArgumentException>(
            () => StrayFileErasure.Refused(StrayErasureFault.RootOutOfReach, "the mount has gone", StrayFileChange.Gone));
        Assert.Equal(
            StrayFileChange.Gone,
            StrayFileErasure.Refused(StrayErasureFault.FileChanged, "it went", StrayFileChange.Gone).Change);
        Assert.Null(StrayFileErasure.Erased(true).Fault);
        Assert.True(StrayFileErasure.Erased(true).FileRemoved);
    }

    private static IntegrityFinding Stray(DateTime? written = null)
        => IntegrityFinding.NoLedgerRow(Check, Primary, StrayPath, 512, Noticed, written ?? Written);
}
