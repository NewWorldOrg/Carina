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
    public void EveryClassEitherWeighedTheFileOrIsTheOneWithNothingToWeigh()
    {
        Assert.Equal(
            Enum.GetValues<IntegrityFault>().Order().ToArray(),
            IntegrityFaults.ThatWeighedTheFile
                .Append(IntegrityFault.FileMissing)
                .Order()
                .ToArray());
    }

    [Fact]
    public void NoClassBothWeighedTheFileAndFoundNothingToWeigh()
    {
        Assert.DoesNotContain(IntegrityFault.FileMissing, IntegrityFaults.ThatWeighedTheFile);
    }

    [Fact]
    public void TheOnlyClassThatNamesNoRecordingIsTheOneWithNoRowBehindIt()
    {
        Assert.Equal(
            [IntegrityFault.NoLedgerRow],
            Enum.GetValues<IntegrityFault>().Except(IntegrityFaults.ThatNameARecording).ToArray());
    }

    [Fact]
    public void TheOnlyClassWithNothingToWeighIsTheOneWhoseFileIsNotThere()
    {
        Assert.Equal(
            [IntegrityFault.FileMissing],
            Enum.GetValues<IntegrityFault>().Except(IntegrityFaults.ThatWeighedTheFile).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
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
    public void EveryClassTheSweepCanNameIsTakenAsItIs(IntegrityFault fault)
    {
        Assert.Equal(fault, IntegrityFaults.Named(fault));
    }

    [Fact]
    public void TheClassesTheSweepCanNameAreTheseFiveAndNoOthers()
    {
        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            Enum.GetNames<IntegrityFault>().Order(StringComparer.Ordinal).ToArray());
    }
}
