using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationDetailTests
{
    [Fact]
    public void AJudgementThatCarriedHasNoLineAmongWhatWasLeftBehind()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationDetail.Of(Run, Carried(MigrationPopulation.Recordings, "7")));
    }
}
