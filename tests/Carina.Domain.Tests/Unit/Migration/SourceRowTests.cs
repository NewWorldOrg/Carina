using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class SourceRowTests
{
    [Fact]
    public void ARowOfTheSourceSystemIsNumberedFromOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Recording(0, InReach));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceRecordingFile(0, "one.m2ts", SourceFileKind.AsBroadcast, 100));
    }
}
