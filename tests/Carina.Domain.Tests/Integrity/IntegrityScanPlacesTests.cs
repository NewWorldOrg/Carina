using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityScanPlacesTests
{
    private const string RecordingFile = "00000001000000000000000000000001.ts";

    private const string Artefact = "00000001000000000000000000000001.0000000a000000000000000000000001.mp4";

    private const string Picture = "00000001000000000000000000000001.jpg";

    [Fact]
    public void AnArtefactNothingClaimsIsAnOrphanOfTheRootItIsIn()
    {
        IntegrityReport report = Compare(
            [Complete(Primary, RecordingFile, 100)],
            [],
            [Holding(Primary, (RecordingFile, 100)), Beside(Encodes, StoragePlace.Encodes, (Artefact, 40))]);

        IntegrityFinding orphan = Assert.Single(report.Findings);

        Assert.Equal(IntegrityFault.NoLedgerRow, orphan.Fault);
        Assert.Equal(Encodes, orphan.Root);
        Assert.Equal(Artefact, orphan.Path);
    }

    [Fact]
    public void AnArtefactTheLedgerClaimsIsLeftAlone()
    {
        IntegrityReport report = Compare(
            [Complete(Primary, RecordingFile, 100)],
            [Declared(Encodes, Artefact)],
            [Holding(Primary, (RecordingFile, 100)), Beside(Encodes, StoragePlace.Encodes, (Artefact, 40))]);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void APictureNothingClaimsIsAnOrphanAndOneThatIsClaimedIsNot()
    {
        IntegrityReport report = Compare(
            [Complete(Primary, RecordingFile, 100)],
            [Declared(Pictures, Picture)],
            [
                Holding(Primary, (RecordingFile, 100)),
                Beside(Pictures, StoragePlace.Thumbnails, (Picture, 8), ("stray.jpg", 8)),
            ]);

        IntegrityFinding orphan = Assert.Single(report.Findings);

        Assert.Equal(Pictures, orphan.Root);
        Assert.Equal("stray.jpg", orphan.Path);
    }

    [Theory]
    [InlineData(StoragePlace.Encodes)]
    [InlineData(StoragePlace.Thumbnails)]
    public void APlaceThatHoldsTheRecordingsOwnFilesIsNotJudgedSoNoRecordingIsCalledAnOrphan(StoragePlace place)
    {
        IntegrityReport report = Compare(
            [Complete(Primary, RecordingFile, 100), StillWriting(Primary, "00000002000000000000000000000001.ts", 2)],
            [],
            [
                Holding(Primary, (RecordingFile, 100)),
                Beside(Encodes, place, (RecordingFile, 100), ("00000002000000000000000000000001.ts", 7), (Artefact, 40)),
            ]);

        Assert.Empty(report.Findings);
        Assert.Equal(1, report.Check.RootsWalked);
        Assert.Equal(1, report.Check.RootsOutOfReach);
        Assert.Equal(1, report.Check.FilesRead);
    }

    [Fact]
    public void ARowIsNeverJudgedAgainstAPlaceThatIsNotARecordingRoot()
    {
        IntegrityReport report = Compare(
            [Complete(Encodes, RecordingFile, 100)],
            [],
            [Beside(Encodes, StoragePlace.Encodes)]);

        Assert.Empty(report.Findings);
        Assert.Equal(0, report.Check.LedgerRowsJudged);
        Assert.Equal(1, report.Check.LedgerRowsInRootsOutOfReach);
    }

    [Fact]
    public void TheRecordingRootIsJudgedAsBeforeBesideThePlacesThisProcessWrites()
    {
        IntegrityReport report = Compare(
            [Complete(Primary, RecordingFile, 100)],
            [],
            [
                Holding(Primary, (RecordingFile, 90), ("left.ts", 5)),
                Beside(Encodes, StoragePlace.Encodes),
                Beside(Pictures, StoragePlace.Thumbnails),
            ]);

        Assert.Equal(
            [IntegrityFault.SizeDisagrees, IntegrityFault.NoLedgerRow],
            report.Findings.Select(finding => finding.Fault).Order().ToArray());
        Assert.All(report.Findings, finding => Assert.Equal(Primary, finding.Root));
        Assert.Equal(3, report.Check.RootsWalked);
    }

    [Fact]
    public void AListingOfAPlaceNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RootListing.Of(Encodes, [], (StoragePlace)0));
    }
}
