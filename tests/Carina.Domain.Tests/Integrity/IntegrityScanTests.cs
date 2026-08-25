using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityScanTests
{
    [Fact]
    public void AFileSmallerThanTheLedgerSaysIsCalledOut()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100, 7)],
            [Holding(Primary, ("one.m2ts", 99))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal("primary", found.Root.Value);
        Assert.Equal("one.m2ts", found.Path);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(100, found.LedgerSize);
        Assert.Equal(99, found.ObservedSize);
        Assert.Equal(At, found.NoticedAt);
        Assert.Equal(Check, found.CheckId);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.FilesRead);
    }

    [Fact]
    public void AFileLargerThanTheLedgerSaysIsCalledOut()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100, 7)],
            [Holding(Primary, ("one.m2ts", 101))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal(100, found.LedgerSize);
        Assert.Equal(101, found.ObservedSize);
    }

    [Fact]
    public void AFileExactlyTheSizeTheLedgerSaysIsLeftAlone()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 100))]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.FilesRead);
        Assert.Equal(1, swept.Check.RootsWalked);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(4_294_967_296L, 4_294_967_296L, false)]
    [InlineData(4_294_967_296L, 4_294_967_295L, true)]
    [InlineData(4_294_967_296L, 4_294_967_297L, true)]
    public void ASizeIsJudgedAgainstTheOneTheLedgerHoldsAndNothingElse(
        long ledgerSize,
        long observedSize,
        bool disagrees)
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", ledgerSize)],
            [Holding(Primary, ("one.m2ts", observedSize))]);

        Assert.Equal(disagrees ? 1 : 0, swept.Findings.Count);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
    }

    [Fact]
    public void AFileNoRowNamesIsCalledAnOrphan()
    {
        IntegrityReport swept = Compare([], [Holding(Primary, ("stray.m2ts", 512))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal("stray.m2ts", found.Path);
        Assert.Null(found.RecordingId);
        Assert.Null(found.LedgerSize);
        Assert.Equal(512, found.ObservedSize);
        Assert.Equal(1, swept.Check.FilesRead);
    }

    [Fact]
    public void AFileInASubdirectoryIsAnOrphanBecauseNoRowCanEverNameIt()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 100), ("thumbnails/one.jpg", 7))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal("thumbnails/one.jpg", found.Path);
        Assert.Equal(7, found.ObservedSize);
        Assert.Equal(2, swept.Check.FilesRead);
    }

    [Fact]
    public void AFileBuriedSeveralDirectoriesDownIsStillAnOrphan()
    {
        IntegrityReport swept = Compare([], [Holding(Primary, ("a/b/c/stray.m2ts", 3))]);

        Assert.Equal("a/b/c/stray.m2ts", Assert.Single(swept.Findings).Path);
    }

    [Fact]
    public void ARowIsNeverAnsweredByAFileOfTheSameNameFurtherDown()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100, 7)],
            [Holding(Primary, ("nested/one.m2ts", 100))]);

        Assert.Equal(
            ["FileMissing", "NoLedgerRow"],
            swept.Findings.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["nested/one.m2ts", "one.m2ts"],
            swept.Findings.Select(finding => finding.Path).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ARowWhoseFileIsNotThereIsCalledMissing()
    {
        IntegrityReport swept = Compare([Complete(Primary, "one.m2ts", 100, 7)], [Empty(Primary)]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.FileMissing, found.Fault);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(100, found.LedgerSize);
        Assert.Null(found.ObservedSize);
        Assert.Equal(0, swept.Check.FilesRead);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
    }

    [Fact]
    public void ARecordingTheLedgerSaysLandedNothingIsNotCalledOutForHoldingNothing()
    {
        IntegrityReport swept = Compare(
            [Failed(Primary, "one.m2ts", 0)],
            [Holding(Primary, ("one.m2ts", 0))]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.FilesRead);
    }

    [Fact]
    public void ARecordingTheLedgerSaysLandedNothingIsStillWeighedAgainstWhatTheLedgerRecorded()
    {
        IntegrityReport swept = Compare(
            [Failed(Primary, "one.m2ts", 5000, 7)],
            [Holding(Primary, ("one.m2ts", 0))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal(5000, found.LedgerSize);
        Assert.Equal(0, found.ObservedSize);
        Assert.Equal(Id(7), found.RecordingId);
    }

    [Fact]
    public void ARecordingTheLedgerSaysLandedSomethingIsCalledEmptyWhenItHoldsNothing()
    {
        IntegrityReport swept = Compare(
            [Truncated(Primary, "one.m2ts", 3000, 7)],
            [Holding(Primary, ("one.m2ts", 0))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.FileEmpty, found.Fault);
        Assert.Equal(3000, found.LedgerSize);
        Assert.Equal(0, found.ObservedSize);
        Assert.Equal(Id(7), found.RecordingId);
    }

    [Fact]
    public void ARecordingTheLedgerCallsCompleteIsCalledOutSeparatelyWhenItHoldsNothing()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 3000, 7)],
            [Holding(Primary, ("one.m2ts", 0))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.EmptyThoughComplete, found.Fault);
        Assert.Equal(3000, found.LedgerSize);
        Assert.Equal(0, found.ObservedSize);
        Assert.Equal(Id(7), found.RecordingId);
    }

    [Fact]
    public void AFileOfOneByteIsNotEmpty()
    {
        IntegrityReport swept = Compare(
            [Truncated(Primary, "one.m2ts", 1)],
            [Holding(Primary, ("one.m2ts", 1))]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
    }

    [Fact]
    public void AnOrphanOfNoSizeIsStillAnOrphan()
    {
        IntegrityReport swept = Compare([], [Holding(Primary, ("stray.m2ts", 0))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal(0, found.ObservedSize);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotCalledASizeMismatch()
    {
        IntegrityReport swept = Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 17))]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsStillWriting);
        Assert.Equal(0, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.FilesRead);
    }

    [Fact]
    public void ARecordingStillBeingWrittenKeepsItsFileFromBeingCalledAnOrphan()
    {
        IntegrityReport swept = Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 17))]);

        Assert.DoesNotContain(swept.Findings, finding => finding.Fault is IntegrityFault.NoLedgerRow);
    }

    [Fact]
    public void ARecordingStillBeingWrittenWithNothingOnDiskYetIsNotCalledMissing()
    {
        IntegrityReport swept = Compare([StillWriting(Primary, "one.m2ts")], [Empty(Primary)]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsStillWriting);
    }

    [Fact]
    public void ARecordingStillBeingWrittenWithAnEmptyFileIsNotCalledEmpty()
    {
        IntegrityReport swept = Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 0))]);

        Assert.Empty(swept.Findings);
    }

    [Fact]
    public void ARootOutOfReachLeavesEverythingUnderItUnjudged()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100)],
            [RootListing.OutOfReach(Primary)]);

        Assert.Empty(swept.Findings);
        Assert.Equal(0, swept.Check.RootsWalked);
        Assert.Equal(1, swept.Check.RootsOutOfReach);
        Assert.Equal(1, swept.Check.LedgerRowsInRootsOutOfReach);
        Assert.Equal(0, swept.Check.LedgerRowsJudged);
    }

    [Fact]
    public void ARootNobodyWalkedLeavesEverythingUnderItUnjudged()
    {
        IntegrityReport swept = Compare([Complete(Bulk, "one.m2ts", 100)], [Empty(Primary)]);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.LedgerRowsInRootsOutOfReach);
        Assert.Equal(1, swept.Check.RootsWalked);
        Assert.Equal(0, swept.Check.RootsOutOfReach);
    }

    [Fact]
    public void AReachableRootIsJudgedEvenWhenAnotherIsOutOfReach()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100, 7), Complete(Bulk, "two.m2ts", 200, 8)],
            [Holding(Primary, ("one.m2ts", 99)), RootListing.OutOfReach(Bulk)]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(1, swept.Check.RootsWalked);
        Assert.Equal(1, swept.Check.RootsOutOfReach);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.LedgerRowsInRootsOutOfReach);
    }

    [Fact]
    public void TheSameNameUnderTwoRootsIsTwoDifferentFiles()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 100)), Holding(Bulk, ("one.m2ts", 100))]);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal("bulk", found.Root.Value);
        Assert.Equal(2, swept.Check.FilesRead);
    }

    [Fact]
    public void OneSweepBringsBackEveryClassOfDisagreementThereIs()
    {
        IntegrityReport swept = Compare(
            [
                Complete(Primary, "disagrees.m2ts", 100, 1),
                Truncated(Primary, "empty.m2ts", 100, 2),
                Complete(Primary, "gone.m2ts", 100, 3),
                Complete(Primary, "agrees.m2ts", 100, 4),
                Complete(Primary, "hollow.m2ts", 100, 5),
                Failed(Primary, "nothing-landed.m2ts", 0, 6),
            ],
            [
                Holding(
                    Primary,
                    ("disagrees.m2ts", 99),
                    ("empty.m2ts", 0),
                    ("agrees.m2ts", 100),
                    ("hollow.m2ts", 0),
                    ("nothing-landed.m2ts", 0),
                    ("stray.m2ts", 5)),
            ]);

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            swept.Findings
                .Select(finding => finding.Fault.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["disagrees.m2ts", "empty.m2ts", "gone.m2ts", "hollow.m2ts", "stray.m2ts"],
            swept.Findings.Select(finding => finding.Path).ToArray());
        Assert.Equal(6, swept.Check.LedgerRowsJudged);
        Assert.Equal(6, swept.Check.FilesRead);
    }

    [Fact]
    public void FindingsComeBackInTheSameOrderWhateverOrderTheyWereFoundIn()
    {
        IntegrityReport swept = Compare(
            [Complete(Bulk, "z.m2ts", 100, 1), Complete(Primary, "a.m2ts", 100, 2)],
            [Holding(Primary, ("a.m2ts", 1), ("c.m2ts", 2)), Holding(Bulk, ("z.m2ts", 3), ("b.m2ts", 4))]);

        Assert.Equal(
            ["bulk/b.m2ts", "bulk/z.m2ts", "primary/a.m2ts", "primary/c.m2ts"],
            swept.Findings.Select(finding => finding.Root.Value + "/" + finding.Path).ToArray());
    }

    [Fact]
    public void ASweepOverNothingSaysItLookedAtNothing()
    {
        IntegrityReport swept = Compare([], []);

        Assert.Empty(swept.Findings);
        Assert.Equal(0, swept.Check.RootsWalked);
        Assert.Equal(0, swept.Check.RootsOutOfReach);
        Assert.Equal(0, swept.Check.FilesRead);
        Assert.Equal(0, swept.Check.LedgerRowsRead);
        Assert.Equal(0, swept.Check.LedgerRowsJudged);
        Assert.Equal(0, swept.Check.LedgerRowsStillWriting);
        Assert.Equal(0, swept.Check.LedgerRowsInRootsOutOfReach);
        Assert.Equal(At, swept.Check.StartedAt);
        Assert.Equal(Done, swept.Check.FinishedAt);
        Assert.Equal(Check, swept.Check.Id);
    }

    [Fact]
    public void AnEmptyLedgerMakesEveryFileAnOrphan()
    {
        IntegrityReport swept = Compare([], [Holding(Primary, ("one.m2ts", 1), ("two.m2ts", 2))]);

        Assert.Equal(2, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault));
        Assert.Equal(2, swept.Check.FilesRead);
    }

    [Fact]
    public void AnEmptyRootLeavesEveryRowMissing()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "one.m2ts", 1, 1), Complete(Primary, "two.m2ts", 2, 2)],
            [Empty(Primary)]);

        Assert.Equal(2, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.FileMissing, finding.Fault));
        Assert.Equal(2, swept.Check.LedgerRowsJudged);
        Assert.Equal(0, swept.Check.FilesRead);
    }

    [Fact]
    public void TheSweepSaysHowMuchItLookedAt()
    {
        IntegrityReport swept = Compare(
            [
                Complete(Primary, "one.m2ts", 100, 1),
                StillWriting(Primary, "two.m2ts", 2),
                Complete(Bulk, "three.m2ts", 300, 3),
            ],
            [Holding(Primary, ("one.m2ts", 100), ("two.m2ts", 4), ("stray.m2ts", 5)), RootListing.OutOfReach(Bulk)]);

        Assert.Equal(1, swept.Check.RootsWalked);
        Assert.Equal(1, swept.Check.RootsOutOfReach);
        Assert.Equal(3, swept.Check.FilesRead);
        Assert.Equal(3, swept.Check.LedgerRowsRead);
        Assert.Equal(1, swept.Check.LedgerRowsJudged);
        Assert.Equal(1, swept.Check.LedgerRowsStillWriting);
        Assert.Equal(1, swept.Check.LedgerRowsInRootsOutOfReach);
        Assert.Single(swept.Findings);
    }

    [Fact]
    public void EveryFindingNamesTheCheckItCameFrom()
    {
        IntegrityReport swept = Compare(
            [Complete(Primary, "gone.m2ts", 100, 1)],
            [Holding(Primary, ("stray.m2ts", 5))]);

        Assert.Equal(2, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(swept.Check.Id, finding.CheckId));
    }

    [Fact]
    public void ACheckNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(null!, [], [], At, Done));
    }

    [Fact]
    public void ALedgerNobodyHandedOverIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(Check, null!, [], At, Done));
    }

    [Fact]
    public void AListingNobodyHandedOverIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(Check, [], null!, At, Done));
    }

    [Fact]
    public void NothingHandedOverAtAllIsStillRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(null!, null!, null!, At, Done));
    }

    [Fact]
    public void AHoleInTheListingsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Compare([], [null!]));
    }

    [Fact]
    public void AHoleInTheLedgerIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Compare([null!], []));
    }

    [Fact]
    public void ARootWalkedTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Compare([], [Empty(Primary), Empty(Primary)]));
    }

    [Fact]
    public void ARootWalkedOnceReachableAndOnceNotIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Compare([], [Empty(Primary), RootListing.OutOfReach(Primary)]));
    }

    [Fact]
    public void OneFileClaimedByTwoRowsIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Compare([Complete(Primary, "one.m2ts", 1, 1), Complete(Primary, "one.m2ts", 2, 2)], []));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare(
                Check,
                [],
                [],
                new DateTime(2026, 8, 26, 3, 0, 0, DateTimeKind.Local),
                Done));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefusedEvenWhenThereIsSomethingToReport()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare(
                Check,
                [],
                [Holding(Primary, ("stray.m2ts", 1))],
                new DateTime(2026, 8, 26, 3, 0, 0, DateTimeKind.Unspecified),
                Done));
    }

    [Fact]
    public void ACheckThatFinishesBeforeItStartsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => IntegrityScan.Compare(Check, [], [], Done, At));
    }
}
