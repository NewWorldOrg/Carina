using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRecordReadingTests
{
    [Fact]
    public void EveryReasonForLeavingSomethingBehindIsNamedEvenWhenNothingWasLeftForIt()
    {
        IReadOnlyList<MigrationRefusalCount> counted = MigrationRefusalCount.EveryOne(
            new Dictionary<MigrationRefusal, int> { [MigrationRefusal.Orphan] = 5 });

        Assert.Equal(
            [.. MigrationRefusals.All],
            counted.Select(one => one.Refusal).ToArray());
        Assert.Equal(5, counted.Single(one => one.Refusal is MigrationRefusal.Orphan).Count);
        Assert.All(
            counted.Where(one => one.Refusal is not MigrationRefusal.Orphan),
            one => Assert.Equal(0, one.Count));
    }

    [Fact]
    public void AReasonTheRecordCannotNameIsRefusedRatherThanCountedAsOneOfTheSeven()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationRefusalCount.EveryOne(new Dictionary<MigrationRefusal, int> { [(MigrationRefusal)99] = 1 }));

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
