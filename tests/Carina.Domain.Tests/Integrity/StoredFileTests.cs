using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class StoredFileTests
{
    [Fact]
    public void AFileKeepsThePathAndTheSizeItWasReadWith()
    {
        var file = new StoredFile("one.m2ts", 100);

        Assert.Equal("one.m2ts", file.Path);
        Assert.Equal(100, file.SizeBytes);
    }

    [Fact]
    public void AFileDeeperDownKeepsThePathItWasReachedBy()
    {
        Assert.Equal("a/b/one.m2ts", new StoredFile("a/b/one.m2ts", 1).Path);
    }

    [Theory]
    [InlineData("2026..08.m2ts")]
    [InlineData("..hidden.m2ts")]
    [InlineData("one.m2ts ")]
    [InlineData(" one.m2ts")]
    [InlineData("   ")]
    [InlineData("a\\b.m2ts")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/../b.m2ts")]
    [InlineData("one\nm2ts")]
    [InlineData("あ.m2ts")]
    [InlineData("-")]
    public void AnyNameTheDiskAllowsIsANameThePointOfComparisonCanCarry(string path)
    {
        Assert.Equal(path, new StoredFile(path, 1).Path);
    }

    [Fact]
    public void APathFarLongerThanAnyLedgerNameIsStillCarried()
    {
        string deep = string.Join("/", Enumerable.Repeat(new string('a', 200), 12));

        Assert.Equal(deep, new StoredFile(deep, 1).Path);
        Assert.True(deep.Length > 2000);
    }

    [Fact]
    public void AFileHoldingNothingIsStillAFile()
    {
        Assert.Equal(0, new StoredFile("one.m2ts", 0).SizeBytes);
    }

    [Fact]
    public void AFileSmallerThanEmptyIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredFile("one.m2ts", -1));
    }

    [Fact]
    public void AFileWithNoPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(string.Empty, 1));
    }

    [Fact]
    public void AFileWithNoPathAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new StoredFile(null!, 1));
    }

    [Theory]
    [InlineData("/srv/recordings/one.m2ts")]
    [InlineData("/one.m2ts")]
    public void APathReadFromTheTopOfTheDiskIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(path, 1));
    }
}
