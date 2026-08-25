using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityFindingTests
{
    private static readonly RecordingFileName Name = new("one.m2ts");

    [Fact]
    public void ASizeDisagreementCarriesBothSizesAndTheRecordingItIsAbout()
    {
        IntegrityFinding finding = IntegrityFinding.SizeDisagrees(Primary, Id(3), Name, 100, 99, At);

        Assert.Equal(IntegrityFault.SizeDisagrees, finding.Fault);
        Assert.Equal(Id(3), finding.RecordingId);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Equal(99, finding.ObservedSize);
        Assert.Equal("one.m2ts", finding.FileName);
        Assert.Equal("primary", finding.Root.Value);
        Assert.Equal(At, finding.NoticedAt);
    }

    [Fact]
    public void AnOrphanNamesNoRecordingAndNoLedgerSize()
    {
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", 512, At);

        Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault);
        Assert.Null(finding.RecordingId);
        Assert.Null(finding.LedgerSize);
        Assert.Equal(512, finding.ObservedSize);
    }

    [Fact]
    public void AMissingFileHasNoObservedSizeBecauseThereWasNothingToWeigh()
    {
        IntegrityFinding finding = IntegrityFinding.FileMissing(Primary, Id(3), Name, 100, At);

        Assert.Equal(IntegrityFault.FileMissing, finding.Fault);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Null(finding.ObservedSize);
        Assert.Equal(Id(3), finding.RecordingId);
    }

    [Fact]
    public void AnEmptyFileKeepsWhatTheLedgerThoughtItWeighed()
    {
        IntegrityFinding finding = IntegrityFinding.FileEmpty(Primary, Id(3), Name, 100, 0, At);

        Assert.Equal(IntegrityFault.FileEmpty, finding.Fault);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Equal(0, finding.ObservedSize);
    }

    [Fact]
    public void TwoFindingsAboutTheSameThingAreTheSameFinding()
    {
        Assert.Equal(
            IntegrityFinding.SizeDisagrees(Primary, Id(3), Name, 100, 99, At),
            IntegrityFinding.SizeDisagrees(Primary, Id(3), Name, 100, 99, At));
    }

    [Fact]
    public void TwoFindingsOfDifferentClassesAreNotTheSameFinding()
    {
        Assert.NotEqual(
            IntegrityFinding.FileMissing(Primary, Id(3), Name, 100, At),
            IntegrityFinding.FileEmpty(Primary, Id(3), Name, 100, 0, At));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ATimeThatIsNotInUtcIsRefused(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityFinding.NoLedgerRow(
                Primary,
                "stray.m2ts",
                1,
                new DateTime(2026, 8, 26, 3, 0, 0, kind)));
    }

    [Fact]
    public void ANegativeObservedSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", -1, At));
    }

    [Fact]
    public void ANegativeLedgerSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFinding.FileMissing(Primary, Id(3), Name, -1, At));
    }

    [Fact]
    public void ARootNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.NoLedgerRow(null!, "stray.m2ts", 1, At));
    }

    [Fact]
    public void ARecordingNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.FileMissing(Primary, null!, Name, 1, At));
    }

    [Fact]
    public void AFileNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.FileMissing(Primary, Id(3), null!, 1, At));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnOrphanWithNoNameIsRefused(string fileName)
    {
        Assert.Throws<ArgumentException>(() => IntegrityFinding.NoLedgerRow(Primary, fileName, 1, At));
    }

    [Fact]
    public void AnOrphanWithNoNameAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityFinding.NoLedgerRow(Primary, null!, 1, At));
    }
}
