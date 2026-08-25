using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class LedgerFileTests
{
    private static readonly RecordingFileName Name = new("one.m2ts");

    [Fact]
    public void ARecordingThatEndedCarriesTheSizeItWasWeighedAtAndWhatTheLedgerClaims()
    {
        LedgerFile row = LedgerFile.Ended(Id(3), Primary, Name, LedgerClaim.EverythingLanded, 100);

        Assert.Equal(100, row.SizeObserved);
        Assert.Equal(LedgerClaim.EverythingLanded, row.Claim);
        Assert.Equal(Id(3), row.Id);
        Assert.Equal("primary", row.Root.Value);
        Assert.Equal("one.m2ts", row.FileName.Value);
    }

    [Theory]
    [InlineData(LedgerClaim.NothingLanded)]
    [InlineData(LedgerClaim.SomethingLanded)]
    [InlineData(LedgerClaim.EverythingLanded)]
    public void EveryClaimTheLedgerCanMakeIsKept(LedgerClaim claim)
    {
        Assert.Equal(claim, LedgerFile.Ended(Id(3), Primary, Name, claim, 1).Claim);
    }

    [Fact]
    public void ARecordingThatEndedEmptyStillCarriesThatSize()
    {
        Assert.Equal(0, LedgerFile.Ended(Id(3), Primary, Name, LedgerClaim.NothingLanded, 0).SizeObserved);
    }

    [Fact]
    public void ARecordingStillBeingWrittenCarriesNeitherSizeNorClaim()
    {
        LedgerFile row = LedgerFile.StillWriting(Id(3), Primary, Name);

        Assert.Null(row.SizeObserved);
        Assert.Null(row.Claim);
    }

    [Fact]
    public void AClaimNobodyHoldsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LedgerFile.Ended(Id(3), Primary, Name, (LedgerClaim)99, 1));
    }

    [Fact]
    public void ASizeSmallerThanEmptyIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LedgerFile.Ended(Id(3), Primary, Name, LedgerClaim.SomethingLanded, -1));
    }

    [Fact]
    public void ARowWithNoRecordingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => LedgerFile.Ended(null!, Primary, Name, LedgerClaim.SomethingLanded, 1));
    }

    [Fact]
    public void ARowWithNoRootIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LedgerFile.StillWriting(Id(3), null!, Name));
    }

    [Fact]
    public void ARowWithNoFileNameIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LedgerFile.StillWriting(Id(3), Primary, null!));
    }

    [Fact]
    public void ARowWithNothingAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LedgerFile.StillWriting(null!, null!, null!));
    }
}
