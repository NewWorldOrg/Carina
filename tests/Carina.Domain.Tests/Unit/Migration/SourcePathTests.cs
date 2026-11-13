using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class SourcePathTests
{
    [Fact]
    public void APathIsReadFromTheSourceOutputDirectoryDown()
    {
        Assert.Throws<ArgumentException>(() => new SourceFile("/elsewhere/one.m2ts", 100));
        Assert.Throws<ArgumentException>(
            () => new SourceRecordingFile(7, "/elsewhere/one.m2ts", SourceFileKind.AsBroadcast, 100));
    }
}
