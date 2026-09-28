using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegrityFaultTests
{
    [Fact]
    public void EveryClassEitherNamesARecordingOrIsTheOneThatCannot()
    {
        Assert.Equal(
            Enum.GetValues<IntegrityFault>().Order().ToArray(),
            IntegrityFaults.ThatNameARecording
                .Append(IntegrityFault.NoLedgerRow)
                .Order()
                .ToArray());
    }

    [Fact]
    public void NoClassBothNamesARecordingAndIsTheOneThatCannot()
    {
        Assert.DoesNotContain(IntegrityFault.NoLedgerRow, IntegrityFaults.ThatNameARecording);
    }

    [Fact]
    public void EveryClassEitherWeighedTheFileOrIsOneWithNothingToWeigh()
    {
        Assert.Equal(
            Enum.GetValues<IntegrityFault>().Order().ToArray(),
            IntegrityFaults.ThatWeighedTheFile
                .Append(IntegrityFault.FileMissing)
                .Append(IntegrityFault.ThumbnailMissing)
                .Order()
                .ToArray());
    }

    [Fact]
    public void NoClassBothWeighedTheFileAndFoundNothingToWeigh()
    {
        Assert.DoesNotContain(IntegrityFault.FileMissing, IntegrityFaults.ThatWeighedTheFile);
        Assert.DoesNotContain(IntegrityFault.ThumbnailMissing, IntegrityFaults.ThatWeighedTheFile);
    }

    [Fact]
    public void TheClassesThatCarryTheLedgerSizeAreTheOnesAboutTheRecordingsOwnFile()
    {
        Assert.Equal(
            [IntegrityFault.ThumbnailMissing],
            IntegrityFaults.ThatNameARecording.Except(IntegrityFaults.ThatCarryTheLedgerSize).ToArray());
        Assert.Empty(IntegrityFaults.ThatCarryTheLedgerSize.Except(IntegrityFaults.ThatNameARecording));
    }

    [Fact]
    public void TheOnlyClassThatNamesNoRecordingIsTheOneWithNoRowBehindIt()
    {
        Assert.Equal(
            [IntegrityFault.NoLedgerRow],
            Enum.GetValues<IntegrityFault>().Except(IntegrityFaults.ThatNameARecording).ToArray());
    }

    [Fact]
    public void TheOnlyClassesWithNothingToWeighAreTheOnesWhoseFileIsNotThere()
    {
        Assert.Equal(
            [IntegrityFault.FileMissing, IntegrityFault.ThumbnailMissing],
            Enum.GetValues<IntegrityFault>().Except(IntegrityFaults.ThatWeighedTheFile).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(-1)]
    public void AClassTheSweepCannotNameIsRefused(int fault)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegrityFaults.Named((IntegrityFault)fault));
    }

    [Theory]
    [InlineData(IntegrityFault.SizeDisagrees)]
    [InlineData(IntegrityFault.NoLedgerRow)]
    [InlineData(IntegrityFault.FileMissing)]
    [InlineData(IntegrityFault.FileEmpty)]
    [InlineData(IntegrityFault.EmptyThoughComplete)]
    [InlineData(IntegrityFault.ThumbnailMissing)]
    public void EveryClassTheSweepCanNameIsTakenAsItIs(IntegrityFault fault)
    {
        Assert.Equal(fault, IntegrityFaults.Named(fault));
    }

    [Fact]
    public void TheClassesTheSweepCanNameAreTheseSixAndNoOthers()
    {
        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees", "ThumbnailMissing"],
            Enum.GetNames<IntegrityFault>().Order(StringComparer.Ordinal).ToArray());
    }
}
