using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeJobQueryTests
{
    [Fact]
    public void NothingAskedForIsTheFirstPageOfTheDefaultSizeOverEveryStanding()
    {
        EncodeJobQuery? query = EncodeJobQuery.For(null, null, null);

        Assert.NotNull(query);
        Assert.Empty(query.Statuses);
        Assert.Equal(1, query.Page);
        Assert.Equal(EncodeJobQuery.DefaultPerPage, query.PerPage);
    }

    [Fact]
    public void APageSizeOverTheCeilingIsCutDownToItAndOneBelowOneIsTheDefault()
    {
        Assert.Equal(EncodeJobQuery.MostPerPage, EncodeJobQuery.For(null, 1, EncodeJobQuery.MostPerPage + 1)!.PerPage);
        Assert.Equal(EncodeJobQuery.DefaultPerPage, EncodeJobQuery.For(null, 1, 0)!.PerPage);
        Assert.Equal(7, EncodeJobQuery.For(null, 3, 7)!.PerPage);
        Assert.Equal(3, EncodeJobQuery.For(null, 3, 7)!.Page);
    }

    [Fact]
    public void APageBelowTheFirstIsNoPageAtAll()
    {
        Assert.Null(EncodeJobQuery.For(null, 0, null));
        Assert.Null(EncodeJobQuery.For(null, -1, null));
    }

    [Fact(DisplayName = "BR-ES-002: the standings asked for are the ledger's own, once each, and nothing cast in from outside")]
    public void TheStandingsAskedForAreTheLedgersOwnOnceEach()
    {
        EncodeJobQuery? asked = EncodeJobQuery.For([EncodeJobStatus.Running, EncodeJobStatus.Queued, EncodeJobStatus.Running], null, null);

        Assert.NotNull(asked);
        Assert.Equal([EncodeJobStatus.Running, EncodeJobStatus.Queued], asked.Statuses);
        Assert.Null(EncodeJobQuery.For([(EncodeJobStatus)99], null, null));
    }
}
