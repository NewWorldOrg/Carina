using System.Runtime.Versioning;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

[SupportedOSPlatform("linux")]
public sealed class FfprobeSourceHeadTests : IDisposable
{
    private const string Source = "/srv/recordings/1872e6a880e94ac6a8f93f740239ef00.ts";

    private static readonly ServiceId Service = new(1064);

    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact(DisplayName = "BR-ED2-013: the head is asked for by key, on the programme's own video, over a little more than the longest skip, and read back by key")]
    public void TheHeadIsAskedForByKeyOnTheProgrammesOwnVideo()
    {
        string[] arguments = [.. FfprobeHeadInvocation.Arguments(Source, Service)];

        Assert.Equal("default=nw=1", arguments[arguments.IndexOf("-of") + 1]);
        Assert.Equal("p:1064:v:0", arguments[arguments.IndexOf("-select_streams") + 1]);
        Assert.Equal("format=start_time:frame=best_effort_timestamp_time", arguments[arguments.IndexOf("-show_entries") + 1]);
        Assert.Equal("%+6", arguments[arguments.IndexOf("-read_intervals") + 1]);
        Assert.Equal(FfprobeHeadInvocation.ReadInterval, arguments[arguments.IndexOf("-read_intervals") + 1]);
        Assert.Equal(FfprobeHeadInvocation.ReadFor.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture), FfprobeHeadInvocation.ReadInterval.TrimStart('%', '+'));
        Assert.Equal(FfmpegEncodeInvocation.VideoStream(Service), arguments[arguments.IndexOf("-select_streams") + 1]);
        Assert.Equal(Source, arguments[arguments.IndexOf("-i") + 1]);
        Assert.True(FfprobeHeadInvocation.ReadFor > EncodeTimeline.MostHeadSkip, "a first picture just beyond the longest skip is read, so it can be refused with its number");
    }

    [Fact(DisplayName = "BR-ED2-006: the first picture ffprobe decoded, not the first packet, is the head — a source that begins at 30499.474078 with its first I frame at 30499.981278 is skipped by 0.5072")]
    public async Task TheFirstPictureDecodedIsTheHead()
    {
        SourceHeadReading reading = await Reading(standIns.Script(
            """
            printf 'best_effort_timestamp_time=30499.981278\n'
            printf 'best_effort_timestamp_time=30500.014644\n'
            printf 'best_effort_timestamp_time=30500.048011\n'
            printf 'start_time=30499.474078\n'
            """));

        Assert.True(reading.Measured, reading.Note);
        Assert.Equal(TimeSpan.FromSeconds(30499.474078), reading.Start);
        Assert.Equal(TimeSpan.FromSeconds(30499.981278), reading.FirstPicture);
        Assert.Equal(TimeSpan.FromSeconds(0.5072), reading.HeadSkip);
    }

    [Fact(DisplayName = "BR-ED2-013: a picture whose timestamp ffprobe could not work out is passed over for the next, and what it complained about while exiting 0 decides nothing")]
    public async Task APictureWithoutATimestampIsPassedOverAndComplaintsDecideNothing()
    {
        SourceHeadReading reading = await Reading(standIns.Script(
            """
            printf '[mpeg2video @ 0x1] Invalid frame dimensions 0x0.\n' >&2
            printf 'best_effort_timestamp_time=N/A\n'
            printf 'best_effort_timestamp_time=30499.981278\n'
            printf 'start_time=30499.474078\n'
            """));

        Assert.True(reading.Measured, reading.Note);
        Assert.Equal(TimeSpan.FromSeconds(0.5072), reading.HeadSkip);
    }

    [Fact(DisplayName = "BR-ED2-006: a head with no picture in reach, or no start, is said nothing about rather than skipped by nothing")]
    public async Task AHeadWithNoPictureOrNoStartIsSaidNothingAbout()
    {
        SourceHeadReading noPicture = await Reading(standIns.Script("printf 'start_time=30499.474078\\n'"));
        SourceHeadReading noStart = await Reading(standIns.Script("printf 'best_effort_timestamp_time=30499.981278\\n'"));
        SourceHeadReading backwards = await Reading(standIns.Script("printf 'best_effort_timestamp_time=30499.1\\nstart_time=30499.474078\\n'"));

        Assert.Equal(SourceHeadFault.SaidNothing, noPicture.Fault);
        Assert.Contains("decoded no picture in the first 6 s", noPicture.Note, StringComparison.Ordinal);
        Assert.Equal(SourceHeadFault.SaidNothing, noStart.Fault);
        Assert.Equal(SourceHeadFault.SaidNothing, backwards.Fault);
        Assert.Null(noPicture.HeadSkip);
    }

    [Fact]
    public async Task AProgrammeThatRefusedSaysWithWhatCode()
    {
        SourceHeadReading reading = await Reading(standIns.Script("printf 'no such file\\n' >&2; exit 1"));

        Assert.Equal(SourceHeadFault.Refused, reading.Fault);
        Assert.Equal(1, reading.ExitCode);
        Assert.Contains("no such file", reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProbeThatIsNotOnThisMachineOrWillNotStopIsAFaultNotAnException()
    {
        SourceHeadReading missing = await Reading(standIns.Named("no-such-programme"));
        SourceHeadReading slow = await Reading(standIns.Script("sleep 60"), TimeSpan.FromMilliseconds(250));

        Assert.Equal(SourceHeadFault.ProgrammeMissing, missing.Fault);
        Assert.DoesNotContain('/', missing.Note);
        Assert.Equal(SourceHeadFault.TimedOut, slow.Fault);
    }

    private Task<SourceHeadReading> Reading(string prober, TimeSpan? longest = null)
        => new FfprobeSourceHead(
                new MachineSettings { Prober = prober, LongestRead = longest ?? TimeSpan.FromSeconds(30) },
                TimeProvider.System)
            .ReadAsync(Source, Service, CancellationToken.None);
}
