using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationRefusalCountTests
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
}
