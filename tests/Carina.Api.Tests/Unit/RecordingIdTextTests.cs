using Carina.Api.Common;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Unit;

public sealed class RecordingIdTextTests
{
    [Fact]
    public void TheNameTheLedgerHoldsIsTheOneThatReadsBack()
    {
        RecordingId id = RecordingId.New();

        Assert.Equal(id, RecordingIdText.Read(id.Wire));
        Assert.Equal(32, id.Wire.Length);
    }

    [Fact]
    public void TheThirtyTwoDigitsAreReadWhicheverCaseTheyAreWrittenIn()
    {
        var id = new RecordingId(new Guid("0123456789abcdef0123456789abcdef"));

        Assert.Equal(id, RecordingIdText.Read("0123456789abcdef0123456789abcdef"));
        Assert.Equal(id, RecordingIdText.Read("0123456789ABCDEF0123456789ABCDEF"));
    }

    [Theory]
    [InlineData("0123456789ab-cdef-0123-456789abcdef")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("{01234567-89ab-cdef-0123-456789abcdef}")]
    [InlineData("(01234567-89ab-cdef-0123-456789abcdef)")]
    public void ANameCarryingSeparatorsIsNotOneTheLedgerHolds(string asked)
        => Assert.Null(RecordingIdText.Read(asked));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdeff")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ANameThatIsNotThirtyTwoHexadecimalDigitsIsRefused(string? asked)
        => Assert.Null(RecordingIdText.Read(asked));

    [Fact]
    public void TheNameNoRecordingCanEverHaveIsRefusedRatherThanBuilt()
    {
        Assert.Null(RecordingIdText.Read("00000000000000000000000000000000"));
        Assert.Throws<ArgumentException>(() => new RecordingId(Guid.Empty));
    }

    [Fact]
    public void TheDescriptionSaysTheShapeTheReaderActuallyTakes()
    {
        Assert.Contains("thirty-two", RecordingIdText.Description, StringComparison.Ordinal);
        Assert.Contains("without separators", RecordingIdText.Description, StringComparison.Ordinal);
    }
}
