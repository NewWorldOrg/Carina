using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingFaultsTests
{
    [Fact]
    public void ADiskThatFilledUnderAFileWithSomethingInItNamesTheDiskAlone()
        => Assert.Equal([RecordingFault.DiskExhausted], RecordingFaults.OfAFullDisk(4_096_000));

    [Fact]
    public void ADiskThatFilledUnderAFileThatCouldNotBeWeighedSaysTheSizeWasNotObserved()
        => Assert.Equal(
            [RecordingFault.DiskExhausted, RecordingFault.SizeUnobserved],
            RecordingFaults.OfAFullDisk(null));

    [Fact]
    public void ADiskThatFilledBeforeAnythingLandedSaysNothingLanded()
        => Assert.Equal(
            [RecordingFault.DiskExhausted, RecordingFault.NothingLanded],
            RecordingFaults.OfAFullDisk(0));
}
