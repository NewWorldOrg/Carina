using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeQueryTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AQueryThatNamesNothingReadsTheFirstPageOfTheWholeLedger()
    {
        ReservationOutcomeQuery query = Assert.IsType<ReservationOutcomeQuery>(ReservationOutcomeQuery.For(null, null));

        Assert.Empty(query.Kinds);
        Assert.Empty(query.Channels);
        Assert.Null(query.Rule);
        Assert.Null(query.From);
        Assert.Null(query.To);
        Assert.Equal(1, query.Page);
        Assert.Equal(ReservationOutcomeQuery.DefaultPerPage, query.PerPage);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(ReservationOutcomeQuery.MostPerPage, ReservationOutcomeQuery.MostPerPage)]
    [InlineData(ReservationOutcomeQuery.MostPerPage + 1, ReservationOutcomeQuery.MostPerPage)]
    [InlineData(int.MaxValue, ReservationOutcomeQuery.MostPerPage)]
    [InlineData(0, ReservationOutcomeQuery.DefaultPerPage)]
    [InlineData(-1, ReservationOutcomeQuery.DefaultPerPage)]
    [InlineData(null, ReservationOutcomeQuery.DefaultPerPage)]
    public void APageSizeIsClampedToTheCeilingRatherThanRefused_BR_RV_003(int? asked, int carried)
    {
        ReservationOutcomeQuery query = Assert.IsType<ReservationOutcomeQuery>(
            ReservationOutcomeQuery.For(null, null, perPage: asked));

        Assert.Equal(carried, query.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void APageBelowTheFirstIsRefused(int page)
    {
        Assert.Null(ReservationOutcomeQuery.For(null, null, page: page));
    }

    [Fact]
    public void ASpanRunsForwardsAndReachesAtMostAYear_BR_RV_003()
    {
        Assert.NotNull(ReservationOutcomeQuery.For(Noon, Noon.AddDays(366)));
        Assert.Null(ReservationOutcomeQuery.For(Noon, Noon.AddDays(366).AddSeconds(1)));
        Assert.Null(ReservationOutcomeQuery.For(Noon, Noon));
        Assert.Null(ReservationOutcomeQuery.For(Noon.AddHours(1), Noon));
    }

    [Fact]
    public void AnOpenEndedSpanIsAllowedOnEitherSide()
    {
        Assert.NotNull(ReservationOutcomeQuery.For(Noon, null));
        Assert.NotNull(ReservationOutcomeQuery.For(null, Noon));
    }

    [Fact]
    public void ASpanIsNamedInUtcOrNotAtAll()
    {
        DateTime local = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Local);
        DateTime unspecified = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Null(ReservationOutcomeQuery.For(local, null));
        Assert.Null(ReservationOutcomeQuery.For(null, unspecified));
    }

    [Fact]
    public void AKindOutsideTheLedgersVocabularyIsRefused()
    {
        Assert.Null(ReservationOutcomeQuery.For(
            null,
            null,
            conditions: new ReservationOutcomeConditions { Kinds = [(ReservationOutcomeKind)99] }));
    }

    [Fact]
    public void AKindAskedForTwiceIsAskedForOnce()
    {
        ReservationOutcomeQuery query = Assert.IsType<ReservationOutcomeQuery>(ReservationOutcomeQuery.For(
            null,
            null,
            conditions: new ReservationOutcomeConditions
            {
                Kinds = [ReservationOutcomeKind.Competing, ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed],
            }));

        Assert.Equal([ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed], query.Kinds);
    }

    [Fact]
    public void MoreChannelsThanTheCeilingAreRefused_BR_RV_003()
    {
        ProgrammeService[] tooMany =
        [
            .. Enumerable.Range(1, ReservationOutcomeQuery.MostChannels + 1)
                .Select(service => new ProgrammeService(32736, service)),
        ];

        Assert.Null(ReservationOutcomeQuery.For(
            null,
            null,
            conditions: new ReservationOutcomeConditions { Channels = tooMany }));
        Assert.NotNull(ReservationOutcomeQuery.For(
            null,
            null,
            conditions: new ReservationOutcomeConditions { Channels = tooMany[..^1] }));
    }

    [Fact]
    public void TheRuleAskedForIsCarriedAsItWasNamed()
    {
        RuleId rule = RuleId.New();

        ReservationOutcomeQuery query = Assert.IsType<ReservationOutcomeQuery>(ReservationOutcomeQuery.For(
            null,
            null,
            conditions: new ReservationOutcomeConditions { Rule = rule }));

        Assert.Equal(rule, query.Rule);
    }
}
