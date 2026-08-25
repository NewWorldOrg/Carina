using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class StoredFileTests
{
    [Fact]
    public void AFileKeepsItsPathAndItsSize()
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
    public void AFileWithNoPathIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(path, 1));
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

    [Theory]
    [InlineData("../one.m2ts")]
    [InlineData("a/../../one.m2ts")]
    [InlineData("..")]
    public void APathThatLeavesTheRoomIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(path, 1));
    }

    [Fact]
    public void APathThatSeparatesItsPartsTheOtherWayIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new StoredFile("a\\one.m2ts", 1));
    }

    [Theory]
    [InlineData(" one.m2ts")]
    [InlineData("one.m2ts ")]
    public void APathWithSurroundingSpaceIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(path, 1));
    }

    [Fact]
    public void APathExactlyAsLongAsTheColumnHoldsIsAllowed()
    {
        Assert.Equal(1024, new StoredFile(new string('a', 1024), 1).Path.Length);
    }

    [Fact]
    public void APathOneCharacterLongerIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new StoredFile(new string('a', 1025), 1));
    }

    [Fact]
    public void APathOneCharacterShorterIsAllowed()
    {
        Assert.Equal(1023, new StoredFile(new string('a', 1023), 1).Path.Length);
    }
}
