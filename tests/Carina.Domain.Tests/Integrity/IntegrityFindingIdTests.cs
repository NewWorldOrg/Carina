using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityFindingIdTests
{
    [Fact]
    public void ANameIsTheSameEveryTimeTheSameFactIsNamed()
    {
        IntegrityFindingId one = IntegrityFindingId.Of(IntegrityFault.FileMissing, Primary, "one.m2ts", Id(3));
        IntegrityFindingId other = IntegrityFindingId.Of(IntegrityFault.FileMissing, Primary, "one.m2ts", Id(3));

        Assert.Equal(one, other);
    }

    [Fact]
    public void AnotherClassOfDisagreementAboutTheSameFileIsAnotherName()
    {
        Assert.NotEqual(
            IntegrityFindingId.Of(IntegrityFault.FileEmpty, Primary, "one.m2ts", Id(3)),
            IntegrityFindingId.Of(IntegrityFault.EmptyThoughComplete, Primary, "one.m2ts", Id(3)));
    }

    [Fact]
    public void TheSameNameUnderAnotherRootIsAnotherName()
    {
        Assert.NotEqual(
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "stray.m2ts", null),
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Bulk, "stray.m2ts", null));
    }

    [Fact]
    public void AnotherFileUnderTheSameRootIsAnotherName()
    {
        Assert.NotEqual(
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "one.m2ts", null),
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "two.m2ts", null));
    }

    [Fact]
    public void AnotherRecordingWritingTheSameNameIsAnotherName()
    {
        Assert.NotEqual(
            IntegrityFindingId.Of(IntegrityFault.FileMissing, Primary, "one.m2ts", Id(3)),
            IntegrityFindingId.Of(IntegrityFault.FileMissing, Primary, "one.m2ts", Id(4)));
    }

    [Fact]
    public void AFindingThatNamesARecordingAndOneThatNamesNoneAreTwoNames()
    {
        Assert.NotEqual(
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "one.m2ts", Id(3)),
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "one.m2ts", null));
    }

    [Fact]
    public void ANameReadsAsTheFifthKindOfIdentifierSoNothingTakesItForARandomOne()
    {
        byte[] named = IntegrityFindingId
            .Of(IntegrityFault.FileMissing, Primary, "one.m2ts", Id(3))
            .Value
            .ToByteArray(bigEndian: true);

        Assert.Equal(0x50, named[6] & 0xF0);
        Assert.Equal(0x80, named[8] & 0xC0);
    }

    [Fact]
    public void AClassNobodyHoldsCannotBeNamed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntegrityFindingId.Of((IntegrityFault)99, Primary, "one.m2ts", null));
    }

    [Fact]
    public void ARootIsRequiredToNameAFinding()
    {
        Assert.Throws<ArgumentNullException>(
            () => IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, null!, "one.m2ts", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void APathIsRequiredToNameAFinding(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, path!, null));
    }

    [Fact]
    public void TheNameAFindingAgainstARecordingGetsIsThisExactOneAndNoOther()
    {
        Assert.Equal(
            new Guid("f90d8cb6-430d-5cff-a4a8-132174679e63"),
            IntegrityFindingId.Of(
                IntegrityFault.FileMissing,
                Primary,
                "one.m2ts",
                new RecordingId(new Guid("00000000-0000-0000-0000-000000000003"))).Value);
    }

    [Fact]
    public void TheNameAFindingAgainstNoRecordingGetsIsThisExactOneAndNoOther()
    {
        Assert.Equal(
            new Guid("203af100-950e-59d1-85e5-ab14b7cee622"),
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, Primary, "stray.m2ts", null).Value);
    }
}
