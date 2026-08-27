using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityFindingQueryTests
{
    [Fact]
    public void AskingForNothingInParticularAsksForTheFirstPageAtTheSizeThisEndpointUses()
    {
        IntegrityFindingQuery query = Assert.IsType<IntegrityFindingQuery>(IntegrityFindingQuery.For(null, null));

        Assert.Equal(1, query.Page);
        Assert.Equal(IntegrityFindingQuery.DefaultPerPage, query.PerPage);
    }

    [Fact]
    public void APageSizeAboveTheCeilingIsCutDownToIt()
    {
        IntegrityFindingQuery query = Assert.IsType<IntegrityFindingQuery>(
            IntegrityFindingQuery.For(null, IntegrityFindingQuery.MostPerPage + 1));

        Assert.Equal(IntegrityFindingQuery.MostPerPage, query.PerPage);
    }

    [Fact]
    public void APageSizeAtTheCeilingIsTakenAsItIs()
    {
        Assert.Equal(
            IntegrityFindingQuery.MostPerPage,
            IntegrityFindingQuery.For(null, IntegrityFindingQuery.MostPerPage)!.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOneIsAnsweredAtTheSizeThisEndpointUses(int perPage)
    {
        Assert.Equal(IntegrityFindingQuery.DefaultPerPage, IntegrityFindingQuery.For(null, perPage)!.PerPage);
    }

    [Fact]
    public void APageSizeOfOneIsTakenAsItIs()
    {
        Assert.Equal(1, IntegrityFindingQuery.For(null, 1)!.PerPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageBeforeTheFirstOneIsNotAPageThisEndpointAnswers(int page)
    {
        Assert.Null(IntegrityFindingQuery.For(page, null));
    }

    [Fact]
    public void ThePageAskedForIsTheOneAnswered()
    {
        Assert.Equal(3, IntegrityFindingQuery.For(3, null)!.Page);
    }

    [Fact]
    public void TheCeilingIsTheOneEveryListInThisSystemShares()
    {
        Assert.Equal(200, IntegrityFindingQuery.MostPerPage);
        Assert.Equal(50, IntegrityFindingQuery.DefaultPerPage);
    }
}
