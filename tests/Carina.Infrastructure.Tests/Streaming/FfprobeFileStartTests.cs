using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfprobeFileStartTests
{
    [Fact(DisplayName = "where the file's clock begins is read by its key, hours into the day or before zero")]
    public void WhereTheFilesClockBeginsIsReadByItsKey()
    {
        Assert.Equal(TimeSpan.FromSeconds(42227.955144), FfprobeFileStart.Of(Said(0, "start_time=42227.955144\n")));
        Assert.Equal(TimeSpan.FromSeconds(-2.6), FfprobeFileStart.Of(Said(0, "duration=10.0\nstart_time=-2.600000\n")));
    }

    [Fact(DisplayName = "an answer without the key, with no number under it, or from a run that failed says nothing")]
    public void AnAnswerWithoutANumberSaysNothing()
    {
        Assert.Null(FfprobeFileStart.Of(Said(0, string.Empty)));
        Assert.Null(FfprobeFileStart.Of(Said(0, "start_time=N/A\n")));
        Assert.Null(FfprobeFileStart.Of(Said(1, "start_time=1.000000\n")));
        Assert.Null(FfprobeFileStart.Of(new ProgrammeSaid(null, ProgrammeFault.TimedOut, "start_time=1.000000\n", string.Empty)));
    }

    private static ProgrammeSaid Said(int exitCode, string said) => new(exitCode, null, said, string.Empty);
}
