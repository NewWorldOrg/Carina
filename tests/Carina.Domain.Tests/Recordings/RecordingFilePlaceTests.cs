using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingFilePlaceTests
{
    [Fact]
    public void AFileSittingInTheRoomItselfIsUnderIt()
    {
        Assert.True(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded", "/disk/recorded/a.ts"));
    }

    [Fact]
    public void ATrailingSeparatorOnTheRoomChangesNothing()
    {
        Assert.True(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded/", "/disk/recorded/a.ts"));
    }

    [Fact]
    public void AFileInAFolderBelowTheRoomIsNotDirectlyUnderIt()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded", "/disk/recorded/2026/a.ts"));
    }

    [Fact]
    public void AWayBackUpOutOfTheRoomIsNotUnderIt()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded", "/disk/recorded/../etc/passwd"));
    }

    [Fact]
    public void ARoomWhoseNameThisOneMerelyStartsWithIsADifferentRoom()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded", "/disk/recorded-elsewhere/a.ts"));
    }

    [Fact]
    public void TheRoomIsNotAFileUnderItself()
    {
        Assert.False(RecordingFilePlace.LiesDirectlyUnder("/disk/recorded", "/disk/recorded"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARoomThatIsNotNamedIsRefusedRatherThanAnswered(string room)
    {
        Assert.Throws<ArgumentException>(() => RecordingFilePlace.LiesDirectlyUnder(room, "/disk/recorded/a.ts"));
    }
}
