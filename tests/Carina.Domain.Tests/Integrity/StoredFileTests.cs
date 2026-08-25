using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class StoredFileTests
{
    [Fact]
    public void AFileKeepsItsNameAndItsSize()
    {
        var file = new StoredFile("one.m2ts", 100);

        Assert.Equal("one.m2ts", file.Name);
        Assert.Equal(100, file.SizeBytes);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AFileWithNoNameIsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(name, 1));
    }

    [Fact]
    public void AFileWithNoNameAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new StoredFile(null!, 1));
    }
}
