using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class FullDiskFaultTests
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
