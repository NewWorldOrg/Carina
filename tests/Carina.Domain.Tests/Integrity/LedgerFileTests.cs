using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class LedgerFileTests
{
    private static readonly RecordingFileName Name = new("one.m2ts");

    [Fact]
    public void ARecordingThatEndedCarriesTheSizeItWasWeighedAt()
    {
        LedgerFile row = LedgerFile.Ended(Id(3), Primary, Name, 100);

        Assert.Equal(100, row.SizeObserved);
        Assert.Equal(Id(3), row.Id);
        Assert.Equal("primary", row.Root.Value);
        Assert.Equal("one.m2ts", row.FileName.Value);
    }

    [Fact]
    public void ARecordingThatEndedEmptyStillCarriesThatSize()
    {
        Assert.Equal(0, LedgerFile.Ended(Id(3), Primary, Name, 0).SizeObserved);
    }

    [Fact]
    public void ARecordingStillBeingWrittenCarriesNoSizeAtAll()
    {
        Assert.Null(LedgerFile.StillWriting(Id(3), Primary, Name).SizeObserved);
    }

    [Fact]
    public void ASizeSmallerThanEmptyIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerFile.Ended(Id(3), Primary, Name, -1));
    }

    [Fact]
    public void ARowWithNoRecordingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LedgerFile.Ended(null!, Primary, Name, 1));
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
