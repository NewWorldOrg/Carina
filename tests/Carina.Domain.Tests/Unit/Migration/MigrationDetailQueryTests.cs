using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationDetailQueryTests
{
    [Fact]
    public void AskingForNoPageAtAllReadsAsTheFirstOne()
    {
        MigrationDetailQuery asked = Assert.IsType<MigrationDetailQuery>(MigrationDetailQuery.For(null, null));

        Assert.Equal(1, asked.Page);
        Assert.Equal(MigrationDetailQuery.DefaultPerPage, asked.PerPage);
    }

    [Fact]
    public void APageBeforeTheFirstIsNotAPage() => Assert.Null(MigrationDetailQuery.For(0, null));

    [Fact]
    public void APageLargerThanTheRecordHandsOutIsCutDownRatherThanRefused()
    {
        MigrationDetailQuery asked = Assert.IsType<MigrationDetailQuery>(
            MigrationDetailQuery.For(2, MigrationDetailQuery.MostPerPage + 1));

        Assert.Equal(2, asked.Page);
        Assert.Equal(MigrationDetailQuery.MostPerPage, asked.PerPage);
    }

    [Fact]
    public void APageOfNoRowsAtAllIsReadAsTheSizeTheRecordChoosesRatherThanAsNothing()
        => Assert.Equal(
            MigrationDetailQuery.DefaultPerPage,
            Assert.IsType<MigrationDetailQuery>(MigrationDetailQuery.For(1, 0)).PerPage);
}
