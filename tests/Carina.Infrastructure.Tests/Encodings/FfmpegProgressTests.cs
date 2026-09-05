using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegProgressTests
{
    /// <summary>
    /// What ffmpeg 6.1.6 actually writes to <c>-progress pipe:1</c>, read off the container on
    /// 2026-09-05: the first block before anything has been written carries N/A throughout, and
    /// out_time_ms holds microseconds — the same number as out_time_us — which is why nothing
    /// here reads out_time_ms.
    /// </summary>
    private const string AsFfmpegWritesIt = """
        frame=0
        fps=0.00
        stream_0_0_q=0.0
        bitrate=N/A
        total_size=0
        out_time_us=N/A
        out_time_ms=N/A
        out_time=N/A
        dup_frames=0
        drop_frames=0
        speed=N/A
        progress=continue
        frame=60
        fps=0.00
        stream_0_0_q=-1.0
        bitrate=N/A
        total_size=N/A
        out_time_us=1966667
        out_time_ms=1966667
        out_time=00:00:01.966667
        dup_frames=0
        drop_frames=0
        speed=  23x
        progress=end

        """;

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(2097.502489);

    [Fact(DisplayName = "BR-ED2-013: how far along is read by key and never by position")]
    public void HowFarAlongIsReadByKeyAndNeverByPosition()
    {
        var reading = new FfmpegProgressReading(Whole);

        EncodeProgress? last = null;

        foreach (string line in AsFfmpegWritesIt.Split('\n'))
        {
            last = reading.Read(line) ?? last;
        }

        Assert.NotNull(last);
        Assert.Equal(TimeSpan.FromSeconds(1.966667), last.Reached);
        Assert.Equal(23, last.Speed);
        Assert.True(last.Ended);
    }

    [Fact]
    public void ABlockComesBackOnlyWhenFfmpegSaysTheBlockIsOver()
    {
        var reading = new FfmpegProgressReading(Whole);

        Assert.Null(reading.Read("out_time_us=1000000"));
        Assert.Null(reading.Read("speed=2.5x"));

        EncodeProgress? progress = reading.Read("progress=continue");

        Assert.NotNull(progress);
        Assert.Equal(TimeSpan.FromSeconds(1), progress.Reached);
        Assert.Equal(2.5, progress.Speed);
        Assert.False(progress.Ended);
    }

    [Fact]
    public void ABlockThatSaysNothingUsableIsNotABlock()
    {
        var reading = new FfmpegProgressReading(Whole);

        Assert.Null(reading.Read("out_time_us=N/A"));
        Assert.Null(reading.Read("speed=N/A"));
        Assert.Null(reading.Read("progress=continue"));
    }

    [Fact]
    public void ASpeedFfmpegCannotYetGiveIsNoSpeedAtAllRatherThanAGuess()
    {
        var reading = new FfmpegProgressReading(Whole);

        reading.Read("out_time_us=1000000");
        reading.Read("speed=N/A");

        EncodeProgress progress = reading.Read("progress=continue")!;

        Assert.Equal(0, progress.Speed);
        Assert.Null(progress.Left);
    }

    [Fact]
    public void EachBlockStartsAgainRatherThanKeepingWhatTheLastOneSaid()
    {
        var reading = new FfmpegProgressReading(Whole);

        reading.Read("out_time_us=1000000");
        reading.Read("speed=2.5x");
        reading.Read("progress=continue");

        reading.Read("out_time_us=2000000");

        EncodeProgress progress = reading.Read("progress=continue")!;

        Assert.Equal(TimeSpan.FromSeconds(2), progress.Reached);
        Assert.Equal(0, progress.Speed);
    }

    [Fact]
    public void AWholeNobodyCouldReadLeavesTheReachedTimeStandingOnItsOwn()
    {
        var reading = new FfmpegProgressReading(null);

        reading.Read("out_time_us=1000000");

        EncodeProgress progress = reading.Read("progress=end")!;

        Assert.Equal(TimeSpan.FromSeconds(1), progress.Reached);
        Assert.Null(progress.Portion);
    }

    [Fact]
    public void ALineThatIsNotAKeyAndAValueIsNotRead()
    {
        var reading = new FfmpegProgressReading(Whole);

        Assert.Null(reading.Read("frame= 60 fps=0.0 q=-1.0 size=N/A time=00:00:01.96"));
        Assert.Null(reading.Read(string.Empty));
        Assert.Null(reading.Read("out_time_us=1000000"));
        Assert.NotNull(reading.Read("progress=continue"));
    }

    [Fact]
    public void ATimeFfmpegWroteAsSomethingOtherThanANumberIsNotATime()
    {
        var reading = new FfmpegProgressReading(Whole);

        reading.Read("out_time_us=-1");

        Assert.Null(reading.Read("progress=continue"));
    }

    [Fact(DisplayName = "BR-ED2-013: the job asks ffmpeg for the block form of progress and not for its status line")]
    public void TheJobAsksFfmpegForTheBlockFormOfProgressAndNotForItsStatusLine()
    {
        string[] arguments = [.. FfmpegEncodeInvocation.Arguments(
            new Domain.Channels.ServiceId(1040),
            AProfile(),
            EncodeEncoder.Software,
            "/srv/recordings/0f8c.ts",
            2,
            TimeSpan.Zero)];

        Assert.Contains("-progress", arguments);
        Assert.Equal("pipe:1", arguments[arguments.IndexOf("-progress") + 1]);
        Assert.Contains("-nostats", arguments);
    }

    private static EncodeProfile AProfile()
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            new DateTime(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc));
}
