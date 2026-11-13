using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingFileNameTests
{
    [Theory]
    [InlineData("a/b.m2ts")]
    [InlineData("a\\b.m2ts")]
    [InlineData("../escaped.m2ts")]
    [InlineData("held..out.m2ts")]
    [InlineData(".")]
    [InlineData(" leading.m2ts")]
    [InlineData("trailing.m2ts ")]
    [InlineData("")]
    public void AFileNameIsASingleNameWithNoWayOutOfItsRoom(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new RecordingFileName(name));

    [Fact]
    public void AFileNameRefusesTheNulByte()
        => Assert.Throws<ArgumentException>(() => new RecordingFileName("recording\0.m2ts"));

    [Fact]
    public void AFileNameIsShortEnoughForAFileSystem()
        => Assert.Throws<ArgumentException>(
            () => new RecordingFileName(new string('a', RecordingFileName.MaxLength + 1)));

    [Fact]
    public void AFileNameBuiltForARecordingCarriesItsId()
    {
        RecordingId id = RecordingId.New();

        RecordingFileName name = RecordingFileName.For(id, ".m2ts");

        Assert.True(name.Names(id));
        Assert.False(name.Names(RecordingId.New()));
    }
}
