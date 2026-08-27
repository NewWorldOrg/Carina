using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingBoundTests
{
    [Fact]
    public void TheBoundsTheseSurfacesHoldAreTheNumbersTheNormNames()
    {
        Assert.Equal(200, RecordingQuery.MostPerPage);
        Assert.Equal(50, RecordingQuery.DefaultPerPage);
        Assert.Equal(64, RecordingQuery.MostChannels);
        Assert.Equal(366, RecordingQuery.LongestSpan.TotalDays);
        Assert.Equal(500, RecordingStopReason.MaxLength);
    }

    [Fact]
    public void APageSizeOfTwoHundredIsTakenAndTwoHundredAndOneIsCutDownToIt()
    {
        Assert.Equal(200, Asked(200));
        Assert.Equal(200, Asked(201));
        Assert.Equal(199, Asked(199));
    }

    [Fact]
    public void AReasonOfFiveHundredLettersIsTakenAndFiveHundredAndOneIsNot()
    {
        Assert.Equal(500, new RecordingStopReason(new string('a', 500)).Value.Length);
        Assert.Null(RecordingStopReason.Read(new string('a', 501)));
    }

    [Fact]
    public void ASpanOfThreeHundredAndSixtySixDaysIsTakenAndOneDayMoreIsNot()
    {
        var began = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.NotNull(RecordingQuery.For(began, began.AddDays(366)));
        Assert.Null(RecordingQuery.For(began, began.AddDays(367)));
    }

    [Fact]
    public void SixtyFourChannelsAreTakenAndSixtyFiveAreNot()
    {
        Assert.NotNull(Over(64));
        Assert.Null(Over(65));
    }

    private static int Asked(int perPage)
    {
        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, perPage: perPage));

        return query.PerPage;
    }

    private static RecordingQuery? Over(int channels)
        => RecordingQuery.For(
            null,
            null,
            conditions: new RecordingConditions
            {
                Channels =
                [
                    .. Enumerable
                        .Range(1, channels)
                        .Select(service => new Carina.Domain.Programmes.ProgrammeService(4, service)),
                ],
            });
}
