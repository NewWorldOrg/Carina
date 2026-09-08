using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingFilePlaceTests
{
    [Fact]
    public void AFileSittingInTheRoomItselfIsUnderIt()
    {
        Assert.True(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes", "/srv/programmes/a.ts"));
    }

    [Fact]
    public void ATrailingSeparatorOnTheRoomChangesNothing()
    {
        Assert.True(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes/", "/srv/programmes/a.ts"));
    }

    [Fact]
    public void AFileInAFolderBelowTheRoomIsNotDirectlyUnderIt()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes", "/srv/programmes/2026/a.ts"));
    }

    [Fact]
    public void AWayBackUpOutOfTheRoomIsNotUnderIt()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes", "/srv/programmes/../etc/passwd"));
    }

    [Fact]
    public void ARoomWhoseNameThisOneMerelyStartsWithIsADifferentRoom()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes", "/srv/programmes-elsewhere/a.ts"));
    }

    [Fact]
    public void TheRoomIsNotAFileUnderItself()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/srv/programmes", "/srv/programmes"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARoomThatIsNotNamedIsRefusedRatherThanAnswered(string room)
    {
        Assert.Throws<ArgumentException>(() => RecordingFilePlace.LiesDirectlyUnder(room, "/srv/programmes/a.ts"));
    }
}
