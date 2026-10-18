using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentContextTests
{
    private static readonly DateTime Started = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "the copy of the programme gives the station, the series, the genres and the guide's start and end in the recording's own time")]
    public void TheProgrammeCopyGivesTheContext()
    {
        ProgrammeCopy programme = new(
            new NetworkId(32736),
            new ServiceId(1024),
            Started.AddMinutes(1),
            Started.AddMinutes(31),
            Started,
            "架空の物語 #3",
            [new ProgrammeGenre(7, 0)],
            [],
            3,
            AudioMode.Stereo,
            null);

        SegmentContext context = SegmentContext.Of(programme, [], CornerLogo.None, []);

        Assert.Equal("32736:1024", context.Station);
        Assert.Equal("架空の物語", context.Series);
        Assert.Equal(TimeSpan.FromMinutes(1), context.ProgrammeStarts);
        Assert.Equal(TimeSpan.FromMinutes(31), context.ProgrammeEnds);
        Assert.Equal(7, Assert.Single(context.Genres).Kind);
    }

    [Fact(DisplayName = "a programme that ends before it starts is refused, and an end not known is left unknown")]
    public void AProgrammeEndsAfterItStarts()
    {
        Assert.Throws<ArgumentException>(() => new SegmentContext(
            new NetworkId(1),
            new ServiceId(1),
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
            [],
            [],
            CornerLogo.None,
            []));
        Assert.Null(new SegmentContext(new NetworkId(1), new ServiceId(1), null, TimeSpan.FromSeconds(60), null, [], [], CornerLogo.None, []).ProgrammeEnds);
    }
}
