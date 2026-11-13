using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationVerdictTests
{
    [Fact]
    public void AReasonIsOneTheRecordCanName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationVerdict.Refuse(
            MigrationPopulation.Recordings,
            "7",
            (MigrationRefusal)99,
            "a programme",
            null,
            null));
    }

    [Fact]
    public void AJudgementNamesWhatItIsAbout()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationVerdict.Carry(MigrationPopulation.Recordings, string.Empty, "a programme", null, null));
    }

    [Fact]
    public void AJudgementWeighsNothingSmallerThanEmpty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationVerdict.Carry(
            MigrationPopulation.Recordings,
            "7",
            "a programme",
            null,
            -1));
    }
}
