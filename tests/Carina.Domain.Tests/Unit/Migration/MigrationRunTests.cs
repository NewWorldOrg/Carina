using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationRunTests
{
    [Fact]
    public void ARunFinishesAfterItStarts()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationRun.Rehydrate(Run, Source, MigrationPass.Rehearsal, Ended, Began));
    }

    [Fact]
    public void ARunKeepsItsTimesInOneTimeZone()
    {
        Assert.Throws<ArgumentException>(() => MigrationRun.Rehydrate(
            Run,
            Source,
            MigrationPass.Rehearsal,
            DateTime.SpecifyKind(Began, DateTimeKind.Local),
            Ended));
    }

    [Fact]
    public void ARunIsEitherARehearsalOrTheRealThing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationRun.Rehydrate(Run, Source, (MigrationPass)9, Began, Ended));
    }
}
