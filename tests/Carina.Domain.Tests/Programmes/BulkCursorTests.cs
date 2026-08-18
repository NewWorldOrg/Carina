using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class BulkCursorTests
{
    [Fact]
    public void ACursorSurvivesBeingWrittenDownAndReadBack()
    {
        var cursor = new BulkCursor(3, 4_242);

        Assert.Equal(cursor, BulkCursor.Read(cursor.Text));
    }

    [Fact]
    public void TheBeginningOfAGenerationCarriesNoRevision()
        => Assert.Equal(0, BulkCursor.Beginning(2).Revision);

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("3:4:5")]
    [InlineData("three:4")]
    [InlineData("3:four")]
    [InlineData("0:4")]
    [InlineData("3:-1")]
    public void ACursorThatDoesNotSayGenerationAndRevisionIsRefused(string text)
        => Assert.Null(BulkCursor.Read(text));

    [Fact]
    public void ARowCountNobodyNamedFallsBackRatherThanFetchingEverything()
        => Assert.Equal(BulkCursor.DefaultRows, BulkCursor.Rows(null));

    [Fact]
    public void ARowCountBeyondTheCeilingIsBroughtDownToIt()
        => Assert.Equal(BulkCursor.MostRows, BulkCursor.Rows(1_000_000));

    [Fact]
    public void ARowCountOfNothingIsNotHonoured()
        => Assert.Equal(BulkCursor.DefaultRows, BulkCursor.Rows(0));
}
