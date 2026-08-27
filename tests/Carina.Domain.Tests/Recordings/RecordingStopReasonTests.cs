using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingStopReasonTests
{
    [Fact]
    public void AReasonIsCarriedWithTheSpaceAroundItTakenOff()
        => Assert.Equal("the wrong programme", new RecordingStopReason("  the wrong programme  ").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void AStopWithNothingSaidAboutItIsNotAReason(string? asked)
    {
        Assert.Null(RecordingStopReason.Read(asked));

        ArgumentException refusal = Assert.ThrowsAny<ArgumentException>(() => new RecordingStopReason(asked!));

        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void AReasonAsLongAsTheCeilingAllowsIsCarried()
    {
        string asked = new('a', RecordingStopReason.MaxLength);

        Assert.Equal(asked, new RecordingStopReason(asked).Value);
    }

    [Fact]
    public void OneLetterOverTheCeilingIsRefused()
    {
        string asked = new('a', RecordingStopReason.MaxLength + 1);

        Assert.Null(RecordingStopReason.Read(asked));

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => new RecordingStopReason(asked));

        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void ALongReasonIsMeasuredAfterTheSpaceAroundItIsTakenOff()
    {
        string asked = " " + new string('a', RecordingStopReason.MaxLength) + " ";

        Assert.Equal(RecordingStopReason.MaxLength, new RecordingStopReason(asked).Value.Length);
    }

    [Theory]
    [InlineData("the recorder\u0007rang")]
    [InlineData("two\nlines")]
    [InlineData("an escape\u001b[2J")]
    public void AReasonSomethingWouldActOnRatherThanShowIsRefused(string asked)
    {
        Assert.Null(RecordingStopReason.Read(asked));

        Assert.Throws<ArgumentException>(() => new RecordingStopReason(asked));
    }

    [Fact]
    public void AReasonThatReadsAsATrimmedStringIsTheSameOneTheReaderGives()
    {
        RecordingStopReason read = Assert.IsType<RecordingStopReason>(RecordingStopReason.Read(" mistake "));

        Assert.Equal(new RecordingStopReason("mistake"), read);
    }
}
