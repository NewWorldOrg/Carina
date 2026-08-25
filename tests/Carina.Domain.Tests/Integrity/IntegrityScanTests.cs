using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityScanTests
{
    [Fact]
    public void AFileSmallerThanTheLedgerSaysIsCalledOut()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100, 7)],
            [Holding(Primary, ("one.m2ts", 99))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal("primary", found.Root.Value);
        Assert.Equal("one.m2ts", found.FileName);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(100, found.LedgerSize);
        Assert.Equal(99, found.ObservedSize);
        Assert.Equal(At, found.NoticedAt);
        Assert.Equal(1, swept.LedgerRowsJudged);
        Assert.Equal(1, swept.FilesRead);
    }

    [Fact]
    public void AFileLargerThanTheLedgerSaysIsCalledOut()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100, 7)],
            [Holding(Primary, ("one.m2ts", 101))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal(100, found.LedgerSize);
        Assert.Equal(101, found.ObservedSize);
    }

    [Fact]
    public void AFileExactlyTheSizeTheLedgerSaysIsLeftAlone()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 100))],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.LedgerRowsJudged);
        Assert.Equal(1, swept.FilesRead);
        Assert.Equal(1, swept.RootsWalked);
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
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", ledgerSize)],
            [Holding(Primary, ("one.m2ts", observedSize))],
            At);

        Assert.Equal(disagrees ? 1 : 0, swept.Findings.Count);
        Assert.Equal(1, swept.LedgerRowsJudged);
    }

    [Fact]
    public void AFileNoRowNamesIsCalledAnOrphan()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [],
            [Holding(Primary, ("stray.m2ts", 512))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal("stray.m2ts", found.FileName);
        Assert.Null(found.RecordingId);
        Assert.Null(found.LedgerSize);
        Assert.Equal(512, found.ObservedSize);
        Assert.Equal(1, swept.FilesRead);
    }

    [Fact]
    public void ARowWhoseFileIsNotThereIsCalledMissing()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100, 7)],
            [Empty(Primary)],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.FileMissing, found.Fault);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(100, found.LedgerSize);
        Assert.Null(found.ObservedSize);
        Assert.Equal(0, swept.FilesRead);
        Assert.Equal(1, swept.LedgerRowsJudged);
    }

    [Fact]
    public void ARowWhoseFileHoldsNothingIsCalledEmpty()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 0, 7)],
            [Holding(Primary, ("one.m2ts", 0))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.FileEmpty, found.Fault);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(0, found.LedgerSize);
        Assert.Equal(0, found.ObservedSize);
    }

    [Fact]
    public void AnEmptyFileTheLedgerThinksIsLargeIsCalledEmptyRatherThanDisagreeing()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 0))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.FileEmpty, found.Fault);
        Assert.Equal(100, found.LedgerSize);
        Assert.Equal(0, found.ObservedSize);
    }

    [Fact]
    public void AFileOfOneByteIsNotEmpty()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 1)],
            [Holding(Primary, ("one.m2ts", 1))],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.LedgerRowsJudged);
    }

    [Fact]
    public void AnOrphanOfNoSizeIsStillAnOrphan()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [],
            [Holding(Primary, ("stray.m2ts", 0))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal(0, found.ObservedSize);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotCalledASizeMismatch()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 17))],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.LedgerRowsStillWriting);
        Assert.Equal(0, swept.LedgerRowsJudged);
        Assert.Equal(1, swept.FilesRead);
    }

    [Fact]
    public void ARecordingStillBeingWrittenKeepsItsFileFromBeingCalledAnOrphan()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 17))],
            At);

        Assert.DoesNotContain(swept.Findings, finding => finding.Fault is IntegrityFault.NoLedgerRow);
    }

    [Fact]
    public void ARecordingStillBeingWrittenWithNothingOnDiskYetIsNotCalledMissing()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Empty(Primary)],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.LedgerRowsStillWriting);
    }

    [Fact]
    public void ARecordingStillBeingWrittenWithAnEmptyFileIsNotCalledEmpty()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [StillWriting(Primary, "one.m2ts")],
            [Holding(Primary, ("one.m2ts", 0))],
            At);

        Assert.Empty(swept.Findings);
    }

    [Fact]
    public void ARootOutOfReachLeavesEverythingUnderItUnjudged()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100)],
            [RootListing.OutOfReach(Primary)],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(0, swept.RootsWalked);
        Assert.Equal(1, swept.RootsOutOfReach);
        Assert.Equal(1, swept.LedgerRowsInRootsOutOfReach);
        Assert.Equal(0, swept.LedgerRowsJudged);
    }

    [Fact]
    public void ARootNobodyWalkedLeavesEverythingUnderItUnjudged()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Bulk, "one.m2ts", 100)],
            [Empty(Primary)],
            At);

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.LedgerRowsInRootsOutOfReach);
        Assert.Equal(1, swept.RootsWalked);
        Assert.Equal(0, swept.RootsOutOfReach);
    }

    [Fact]
    public void AReachableRootIsJudgedEvenWhenAnotherIsOutOfReach()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100, 7), Ended(Bulk, "two.m2ts", 200, 8)],
            [Holding(Primary, ("one.m2ts", 99)), RootListing.OutOfReach(Bulk)],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(Id(7), found.RecordingId);
        Assert.Equal(1, swept.RootsWalked);
        Assert.Equal(1, swept.RootsOutOfReach);
        Assert.Equal(1, swept.LedgerRowsJudged);
        Assert.Equal(1, swept.LedgerRowsInRootsOutOfReach);
    }

    [Fact]
    public void TheSameNameUnderTwoRootsIsTwoDifferentFiles()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 100)],
            [Holding(Primary, ("one.m2ts", 100)), Holding(Bulk, ("one.m2ts", 100))],
            At);

        IntegrityFinding found = Assert.Single(swept.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, found.Fault);
        Assert.Equal("bulk", found.Root.Value);
        Assert.Equal(2, swept.FilesRead);
    }

    [Fact]
    public void OneSweepBringsBackEveryClassOfDisagreementThereIs()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [
                Ended(Primary, "disagrees.m2ts", 100, 1),
                Ended(Primary, "empty.m2ts", 100, 2),
                Ended(Primary, "gone.m2ts", 100, 3),
                Ended(Primary, "agrees.m2ts", 100, 4),
            ],
            [
                Holding(
                    Primary,
                    ("disagrees.m2ts", 99),
                    ("empty.m2ts", 0),
                    ("agrees.m2ts", 100),
                    ("stray.m2ts", 5)),
            ],
            At);

        Assert.Equal(
            ["FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            swept.Findings
                .Select(finding => finding.Fault.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["disagrees.m2ts", "empty.m2ts", "gone.m2ts", "stray.m2ts"],
            swept.Findings.Select(finding => finding.FileName).ToArray());
        Assert.Equal(4, swept.LedgerRowsJudged);
        Assert.Equal(4, swept.FilesRead);
    }

    [Fact]
    public void FindingsComeBackInTheSameOrderWhateverOrderTheyWereFoundIn()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Bulk, "z.m2ts", 100, 1), Ended(Primary, "a.m2ts", 100, 2)],
            [Holding(Primary, ("a.m2ts", 1), ("c.m2ts", 2)), Holding(Bulk, ("z.m2ts", 3), ("b.m2ts", 4))],
            At);

        Assert.Equal(
            ["bulk/b.m2ts", "bulk/z.m2ts", "primary/a.m2ts", "primary/c.m2ts"],
            swept.Findings.Select(finding => finding.Root.Value + "/" + finding.FileName).ToArray());
    }

    [Fact]
    public void ASweepOverNothingSaysItLookedAtNothing()
    {
        IntegritySweep swept = IntegrityScan.Compare([], [], At);

        Assert.Empty(swept.Findings);
        Assert.Equal(0, swept.RootsWalked);
        Assert.Equal(0, swept.RootsOutOfReach);
        Assert.Equal(0, swept.FilesRead);
        Assert.Equal(0, swept.LedgerRowsRead);
        Assert.Equal(0, swept.LedgerRowsJudged);
        Assert.Equal(0, swept.LedgerRowsStillWriting);
        Assert.Equal(0, swept.LedgerRowsInRootsOutOfReach);
        Assert.Equal(At, swept.RanAt);
    }

    [Fact]
    public void AnEmptyLedgerMakesEveryFileAnOrphan()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [],
            [Holding(Primary, ("one.m2ts", 1), ("two.m2ts", 2))],
            At);

        Assert.Equal(2, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault));
        Assert.Equal(2, swept.FilesRead);
    }

    [Fact]
    public void AnEmptyRootLeavesEveryRowMissing()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [Ended(Primary, "one.m2ts", 1, 1), Ended(Primary, "two.m2ts", 2, 2)],
            [Empty(Primary)],
            At);

        Assert.Equal(2, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.FileMissing, finding.Fault));
        Assert.Equal(2, swept.LedgerRowsJudged);
        Assert.Equal(0, swept.FilesRead);
    }

    [Fact]
    public void TheSweepSaysHowMuchItLookedAt()
    {
        IntegritySweep swept = IntegrityScan.Compare(
            [
                Ended(Primary, "one.m2ts", 100, 1),
                StillWriting(Primary, "two.m2ts", 2),
                Ended(Bulk, "three.m2ts", 300, 3),
            ],
            [Holding(Primary, ("one.m2ts", 100), ("two.m2ts", 4), ("stray.m2ts", 5)), RootListing.OutOfReach(Bulk)],
            At);

        Assert.Equal(1, swept.RootsWalked);
        Assert.Equal(1, swept.RootsOutOfReach);
        Assert.Equal(3, swept.FilesRead);
        Assert.Equal(3, swept.LedgerRowsRead);
        Assert.Equal(1, swept.LedgerRowsJudged);
        Assert.Equal(1, swept.LedgerRowsStillWriting);
        Assert.Equal(1, swept.LedgerRowsInRootsOutOfReach);
        Assert.Single(swept.Findings);
    }

    [Fact]
    public void ALedgerNobodyHandedOverIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(null!, [], At));
    }

    [Fact]
    public void AListingNobodyHandedOverIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare([], null!, At));
    }

    [Fact]
    public void NeitherSideHandedOverIsStillRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare(null!, null!, At));
    }

    [Fact]
    public void AHoleInTheListingsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare([], [null!], At));
    }

    [Fact]
    public void AHoleInTheLedgerIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityScan.Compare([null!], [], At));
    }

    [Fact]
    public void ARootWalkedTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare([], [Empty(Primary), Empty(Primary)], At));
    }

    [Fact]
    public void ARootWalkedOnceReachableAndOnceNotIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare([], [Empty(Primary), RootListing.OutOfReach(Primary)], At));
    }

    [Fact]
    public void OneFileClaimedByTwoRowsIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare(
                [Ended(Primary, "one.m2ts", 1, 1), Ended(Primary, "one.m2ts", 2, 2)],
                [],
                At));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare([], [], new DateTime(2026, 8, 26, 3, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefusedEvenWhenThereIsSomethingToReport()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityScan.Compare(
                [],
                [Holding(Primary, ("stray.m2ts", 1))],
                new DateTime(2026, 8, 26, 3, 0, 0, DateTimeKind.Unspecified)));
    }
}
