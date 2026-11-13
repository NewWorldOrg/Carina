using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class TranscoderExitTests
{
    [Fact]
    public void AProgrammeThatExitedZeroRanToTheEnd()
    {
        TranscoderExit ended = TranscoderExit.Finished();

        Assert.True(ended.RanToTheEnd);
        Assert.Null(ended.Fault);
        Assert.Equal(0, ended.ExitCode);
    }

    [Fact]
    public void AProgrammeThatRefusedSaysWithWhatCodeAndWhatItComplainedOf()
    {
        TranscoderExit refused = TranscoderExit.Refused(1, "Invalid data found when processing input");

        Assert.False(refused.RanToTheEnd);
        Assert.Equal(TranscoderFault.Refused, refused.Fault);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("Invalid data", refused.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AProgrammeThatExitedZeroWasNotRefusedByIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscoderExit.Refused(0, "fine"));
    }

    [Fact]
    public void AProgrammeThatWasCalledOffHasNoCodeToReport()
    {
        TranscoderExit off = TranscoderExit.CalledOff("nothing said");

        Assert.False(off.RanToTheEnd);
        Assert.Equal(TranscoderFault.CalledOff, off.Fault);
        Assert.Null(off.ExitCode);
    }
}
