using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationQueryTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AQueryThatNamesNothingReadsTheFirstPageOfEverything()
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(null, null));

        Assert.Empty(query.Standings);
        Assert.Null(query.Origin);
        Assert.Empty(query.Channels);
        Assert.Null(query.Keyword);
        Assert.Equal(1, query.Page);
        Assert.Equal(ReservationQuery.DefaultPerPage, query.PerPage);
        Assert.Equal(ReservationSort.StartAt, query.Sort);
        Assert.False(query.Descending);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(ReservationQuery.MostPerPage - 1, ReservationQuery.MostPerPage - 1)]
    [InlineData(ReservationQuery.MostPerPage, ReservationQuery.MostPerPage)]
    [InlineData(null, ReservationQuery.DefaultPerPage)]
    public void APageSizeWithinTheCeilingIsTheOneThatWasAskedFor(int? asked, int carried)
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(null, null, perPage: asked));

        Assert.Equal(carried, query.PerPage);
    }

    [Theory]
    [InlineData(ReservationQuery.MostPerPage + 1)]
    [InlineData(int.MaxValue)]
    public void APageSizeAboveTheCeilingIsCutDownToItRatherThanRefused(int asked)
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(null, null, perPage: asked));

        Assert.Equal(ReservationQuery.MostPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOneIsReadAsTheSizeNobodyAskedForAnythingElseThan(int asked)
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(null, null, perPage: asked));

        Assert.Equal(ReservationQuery.DefaultPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageBeforeTheFirstIsNoPageAtAll(int page)
    {
        Assert.Null(ReservationQuery.For(null, null, page: page));
    }

    [Fact]
    public void ASpanLongerThanTheCeilingIsRefused()
    {
        Assert.Null(ReservationQuery.For(Noon, Noon + ReservationQuery.LongestSpan + TimeSpan.FromSeconds(1)));
        Assert.NotNull(ReservationQuery.For(Noon, Noon + ReservationQuery.LongestSpan));
    }

    [Fact]
    public void ASpanThatRunsBackwardsOrNowhereIsRefused()
    {
        Assert.Null(ReservationQuery.For(Noon, Noon.AddHours(-1)));
        Assert.Null(ReservationQuery.For(Noon, Noon));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AnEdgeOfTheSpanThatIsNotAUtcInstantIsRefused(DateTimeKind kind)
    {
        DateTime askew = DateTime.SpecifyKind(Noon, kind);

        Assert.Null(ReservationQuery.For(askew, null));
        Assert.Null(ReservationQuery.For(null, askew));
    }

    [Fact]
    public void OneEdgeOfTheSpanOnItsOwnIsAllowed()
    {
        Assert.NotNull(ReservationQuery.For(Noon, null));
        Assert.NotNull(ReservationQuery.For(null, Noon));
    }

    [Fact]
    public void AStandingOutsideTheOnesThisEndpointNamesIsRefused()
    {
        Assert.Null(ReservationQuery.For(
            null,
            null,
            conditions: new ReservationConditions { Standings = [(ReservationStanding)99] }));
    }

    [Fact]
    public void EveryStandingThisDomainNamesIsAcceptedAsAFilter()
    {
        foreach (ReservationStanding standing in Enum.GetValues<ReservationStanding>())
        {
            ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(
                null,
                null,
                conditions: new ReservationConditions { Standings = [standing] }));

            Assert.Equal([standing], query.Standings);
        }
    }

    [Fact]
    public void TheSameStandingAskedForTwiceIsAskedForOnce()
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(ReservationQuery.For(
            null,
            null,
            conditions: new ReservationConditions
            {
                Standings = [ReservationStanding.Conflict, ReservationStanding.Conflict],
            }));

        Assert.Equal([ReservationStanding.Conflict], query.Standings);
    }

    [Fact]
    public void AnOriginOutsideTheTwoThereAreIsRefused()
    {
        Assert.Null(ReservationQuery.For(
            null,
            null,
            conditions: new ReservationConditions { Origin = (ReservationOrigin)99 }));
    }

    [Fact]
    public void ASortOutsideTheOnesThisEndpointNamesIsRefused()
    {
        Assert.Null(ReservationQuery.For(null, null, sort: (ReservationSort)99));
    }

    [Fact]
    public void MoreChannelsThanTheCeilingIsRefused()
    {
        ProgrammeService[] many =
        [
            .. Enumerable
                .Range(0, ReservationQuery.MostChannels + 1)
                .Select(index => new ProgrammeService(32736, 1000 + index)),
        ];

        Assert.Null(ReservationQuery.For(null, null, conditions: new ReservationConditions { Channels = many }));
        Assert.NotNull(ReservationQuery.For(
            null,
            null,
            conditions: new ReservationConditions { Channels = many[..ReservationQuery.MostChannels] }));
    }

    [Theory]
    [InlineData("a")]
    [InlineData(" a ")]
    public void AKeywordShorterThanTheFloorIsRefused(string asked)
    {
        Assert.Null(ReservationQuery.For(null, null, conditions: new ReservationConditions { Keyword = asked }));
    }

    [Theory]
    [InlineData("ab", "ab")]
    [InlineData("  drama  ", "drama")]
    public void AKeywordAtOrAboveTheFloorIsCarriedTrimmed(string asked, string carried)
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(
            ReservationQuery.For(null, null, conditions: new ReservationConditions { Keyword = asked }));

        Assert.Equal(carried, query.Keyword);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AKeywordNobodyTypedIsNoKeywordRatherThanARefusal(string? asked)
    {
        ReservationQuery query = Assert.IsType<ReservationQuery>(
            ReservationQuery.For(null, null, conditions: new ReservationConditions { Keyword = asked }));

        Assert.Null(query.Keyword);
    }
}
