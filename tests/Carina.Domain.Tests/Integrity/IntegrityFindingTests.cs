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
        IntegrityFinding finding = IntegrityFinding.SizeDisagrees(Check, Primary, Id(3), Name, 100, 99, At);

        Assert.Equal(IntegrityFault.SizeDisagrees, finding.Fault);
        Assert.Equal(Id(3), finding.RecordingId);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Equal(99, finding.ObservedSize);
        Assert.Equal("one.m2ts", finding.Path);
        Assert.Equal("primary", finding.Root.Value);
        Assert.Equal(At, finding.NoticedAt);
        Assert.Equal(Check, finding.CheckId);
        Assert.NotNull(finding.Id);
    }

    [Fact]
    public void AnOrphanNamesNoRecordingAndNoLedgerSize()
    {
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(Check, Primary, "nested/stray.m2ts", 512, At);

        Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault);
        Assert.Null(finding.RecordingId);
        Assert.Null(finding.LedgerSize);
        Assert.Equal(512, finding.ObservedSize);
        Assert.Equal("nested/stray.m2ts", finding.Path);
    }

    [Fact]
    public void AMissingFileHasNoObservedSizeBecauseThereWasNothingToWeigh()
    {
        IntegrityFinding finding = IntegrityFinding.FileMissing(Check, Primary, Id(3), Name, 100, At);

        Assert.Equal(IntegrityFault.FileMissing, finding.Fault);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Null(finding.ObservedSize);
        Assert.Equal(Id(3), finding.RecordingId);
    }

    [Fact]
    public void AnEmptyFileKeepsWhatTheLedgerThoughtItWeighed()
    {
        IntegrityFinding finding = IntegrityFinding.FileEmpty(Check, Primary, Id(3), Name, 100, 0, At);

        Assert.Equal(IntegrityFault.FileEmpty, finding.Fault);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Equal(0, finding.ObservedSize);
    }

    [Fact]
    public void AnEmptyFileTheLedgerCallsCompleteIsItsOwnClass()
    {
        IntegrityFinding finding = IntegrityFinding.EmptyThoughComplete(Check, Primary, Id(3), Name, 100, 0, At);

        Assert.Equal(IntegrityFault.EmptyThoughComplete, finding.Fault);
        Assert.Equal(100, finding.LedgerSize);
        Assert.Equal(0, finding.ObservedSize);
    }

    [Fact]
    public void TwoFindingsAreTwoFindingsEvenWhenTheySayTheSameThing()
    {
        IntegrityFinding one = IntegrityFinding.FileMissing(Check, Primary, Id(3), Name, 100, At);
        IntegrityFinding other = IntegrityFinding.FileMissing(Check, Primary, Id(3), Name, 100, At);

        Assert.NotEqual(one.Id, other.Id);
    }

    [Fact]
    public void AFindingReadBackKeepsEverythingItWasWrittenWith()
    {
        var id = new IntegrityFindingId(new Guid("11111111-2222-3333-4444-555555555555"));
        IntegrityFinding finding = IntegrityFinding.Rehydrate(
            id,
            Check,
            IntegrityFault.EmptyThoughComplete,
            Primary,
            "one.m2ts",
            Id(3),
            100,
            0,
            At);

        Assert.Equal(id, finding.Id);
        Assert.Equal(Check, finding.CheckId);
        Assert.Equal(IntegrityFault.EmptyThoughComplete, finding.Fault);
        Assert.Equal(Id(3), finding.RecordingId);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ATimeThatIsNotInUtcIsRefused(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityFinding.NoLedgerRow(
                Check,
                Primary,
                "stray.m2ts",
                1,
                new DateTime(2026, 8, 26, 3, 0, 0, kind)));
    }

    [Fact]
    public void AFaultNobodyHoldsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFinding.Rehydrate(
                IntegrityFindingId.New(),
                Check,
                (IntegrityFault)99,
                Primary,
                "one.m2ts",
                null,
                null,
                1,
                At));
    }

    [Fact]
    public void ANegativeObservedSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFinding.NoLedgerRow(Check, Primary, "stray.m2ts", -1, At));
    }

    [Fact]
    public void ANegativeLedgerSizeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFinding.FileMissing(Check, Primary, Id(3), Name, -1, At));
    }

    [Fact]
    public void APathFarLongerThanAnyLedgerNameIsCarriedAsItIs()
    {
        string deep = new('a', 4096);

        Assert.Equal(deep, IntegrityFinding.NoLedgerRow(Check, Primary, deep, 1, At).Path);
    }

    [Theory]
    [InlineData("2026..08.m2ts")]
    [InlineData("one.m2ts ")]
    [InlineData("a\\b.m2ts")]
    [InlineData("   ")]
    public void AnyNameTheDiskAllowsIsOneAFindingCanName(string path)
    {
        Assert.Equal(path, IntegrityFinding.NoLedgerRow(Check, Primary, path, 1, At).Path);
    }

    [Fact]
    public void ACheckNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.NoLedgerRow(null!, Primary, "stray.m2ts", 1, At));
    }

    [Fact]
    public void ARootNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.NoLedgerRow(Check, null!, "stray.m2ts", 1, At));
    }

    [Fact]
    public void ARecordingNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.FileMissing(Check, Primary, null!, Name, 1, At));
    }

    [Fact]
    public void AFileNobodyNamedIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.FileMissing(Check, Primary, Id(3), null!, 1, At));
    }

    [Fact]
    public void AnOrphanWithNoPathIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => IntegrityFinding.NoLedgerRow(Check, Primary, string.Empty, 1, At));
    }

    [Fact]
    public void AnOrphanWithNoPathAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IntegrityFinding.NoLedgerRow(Check, Primary, null!, 1, At));
    }

    [Fact]
    public void AFindingWithNoIdAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFinding.Rehydrate(null!, Check, IntegrityFault.NoLedgerRow, Primary, "a", null, null, 1, At));
    }
}
