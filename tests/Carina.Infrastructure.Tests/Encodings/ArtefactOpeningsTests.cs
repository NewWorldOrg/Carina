using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ArtefactOpeningsTests
{
    private static readonly OutputRoot Encodes = new("encodes");

    [Fact]
    public void BrEd2019TheLastOpeningOfAFileIsKeptForThatFileUnderThatRootAlone()
    {
        HandTurnedClock clock = new(new DateTimeOffset(2026, 10, 4, 3, 0, 0, TimeSpan.Zero));
        ArtefactOpenings reads = new(clock);

        reads.Opened(Encodes, "a.mp4");
        clock.Turn(TimeSpan.FromMinutes(5));
        reads.Opened(Encodes, "a.mp4");

        Assert.Equal(clock.GetUtcNow(), reads.LastOpened(Encodes, "a.mp4"));
        Assert.Null(reads.LastOpened(Encodes, "b.mp4"));
        Assert.Null(reads.LastOpened(new OutputRoot("bulk"), "a.mp4"));
    }
}
