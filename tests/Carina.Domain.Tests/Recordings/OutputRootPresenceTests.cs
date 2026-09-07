using Carina.Contracts;

using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class OutputRootPresenceTests
{
    private static readonly OutputRoot Recorded = new("recorded");

    [Fact]
    public void ARootTheOwningProcessDoesNotDeclareIsNotOneToRemoveAnythingUnder()
    {
        RecordingId mine = RecordingId.New();

        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("elsewhere")],
            Recorded,
            reachable: true,
            [Beside()],
            mine);

        Assert.Equal(RootAbsence.Undeclared, absent);
    }

    [Fact]
    public void ARootNothingCouldBeReadFromIsNotOneToRemoveAnythingUnder()
    {
        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: false,
            [],
            RecordingId.New());

        Assert.Equal(RootAbsence.OutOfReach, absent);
    }

    [Fact]
    public void ARootHoldingNothingBesideTheRecordingAskedForIsWhatALostMountLooksLike()
    {
        RecordingId mine = RecordingId.New();

        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: true,
            [RecordingFile.Of(mine.Wire)],
            mine);

        Assert.Equal(RootAbsence.HoldsNothingBeside, absent);
    }

    [Fact]
    public void ARootThatIsEmptyAltogetherIsWhatALostMountLooksLike()
    {
        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: true,
            [],
            RecordingId.New());

        Assert.Equal(RootAbsence.HoldsNothingBeside, absent);
    }

    [Fact]
    public void ARootHoldingAnotherRecordingIsAMountThatIsReallyThere()
    {
        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: true,
            [Beside()],
            RecordingId.New());

        Assert.Null(absent);
    }

    [Fact]
    public void AFileOfTheRecordingAskedForCountsForNothingWhateverItIsCalled()
    {
        RecordingId mine = RecordingId.New();

        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: true,
            [RecordingFile.Of(mine.Wire), mine.Wire + ".m2ts", mine.Wire + ".ts.tmp"],
            mine);

        Assert.Equal(RootAbsence.HoldsNothingBeside, absent);
    }

    [Fact]
    public void AFileInAFolderUnderTheRootStillSaysTheMountIsThere()
    {
        RecordingId mine = RecordingId.New();

        RootAbsence? absent = OutputRootPresence.Missing(
            [Declared("recorded")],
            Recorded,
            reachable: true,
            [$"2026-09/{RecordingFile.Of(RecordingId.New().Wire)}"],
            mine);

        Assert.Null(absent);
    }

    [Fact]
    public void ADeclarationNobodyCouldReadIsNotADeclarationThatNamesTheRoot()
    {
        RootAbsence? absent = OutputRootPresence.Missing(
            null,
            Recorded,
            reachable: true,
            [Beside()],
            RecordingId.New());

        Assert.Equal(RootAbsence.Undeclared, absent);
    }

    private static string Beside() => RecordingFile.Of(RecordingId.New().Wire);

    private static StorageRootDto Declared(string name) => new() { Name = name, Writable = true };
}
