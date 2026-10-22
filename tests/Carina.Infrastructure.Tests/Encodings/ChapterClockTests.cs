using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ChapterClockTests
{
    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5);

    [Fact(DisplayName = "a moment goes into the metadata file with the head skip added back on")]
    public void AMomentGoesIntoTheMetadataFileWithTheHeadSkipAddedBackOn()
    {
        Assert.Equal(HeadSkip, ChapterClock.InTheMetadata(TimeSpan.Zero, HeadSkip));
        Assert.Equal(TimeSpan.FromSeconds(30) + HeadSkip, ChapterClock.InTheMetadata(TimeSpan.FromSeconds(30), HeadSkip));
        Assert.Equal(TimeSpan.FromSeconds(30), ChapterClock.InTheMetadata(TimeSpan.FromSeconds(30), TimeSpan.Zero));
    }

    [Fact]
    public void AMomentPutIntoTheMetadataFileIsOneOnTheArtefactAndASkipThatGoesForwards()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterClock.InTheMetadata(TimeSpan.FromSeconds(-1), HeadSkip));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChapterClock.InTheMetadata(TimeSpan.Zero, TimeSpan.FromSeconds(-1)));
    }
}
