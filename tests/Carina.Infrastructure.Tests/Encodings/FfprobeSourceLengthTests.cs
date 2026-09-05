using System.Runtime.Versioning;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

[SupportedOSPlatform("linux")]
public sealed class FfprobeSourceLengthTests : IDisposable
{
    private const string Source = "/srv/recordings/1872e6a880e94ac6a8f93f740239ef00.ts";

    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact(DisplayName = "BR-ED2-013: the whole is asked for by key and read back by key")]
    public void TheWholeIsAskedForByKeyAndReadBackByKey()
    {
        string[] arguments = [.. FfprobeLengthInvocation.Arguments(Source)];

        Assert.Equal("default=nw=1", arguments[arguments.IndexOf("-of") + 1]);
        Assert.Equal("format=duration", arguments[arguments.IndexOf("-show_entries") + 1]);
        Assert.Equal(Source, arguments[arguments.IndexOf("-i") + 1]);
    }

    [Fact(DisplayName = "BR-ED2-013: a source that measures 2097.502489 seconds is 2097.502489 seconds")]
    public async Task ASourceThatMeasuresIsTheLengthItMeasured()
    {
        SourceLengthReading reading = await Reading(standIns.Script("printf 'duration=2097.502489\\n'"));

        Assert.True(reading.Measured);
        Assert.Equal(2097.502489, reading.Length!.Value.TotalSeconds, 6);
    }

    [Fact(DisplayName = "BR-ED2-013: what ffprobe complained about while exiting 0 does not make the reading a failure")]
    public async Task WhatFfprobeComplainedAboutWhileExitingZeroDoesNotMakeTheReadingAFailure()
    {
        SourceLengthReading reading = await Reading(standIns.Script(
            """
            printf '[h264 @ 0x1] non-existing PPS 0 referenced\n' >&2
            printf '[h264 @ 0x1] decode_slice_header error\n' >&2
            printf 'duration=2097.502489\n'
            """));

        Assert.True(reading.Measured);
        Assert.Null(reading.Fault);
    }

    [Fact]
    public async Task AKeyOtherThanTheOneAskedForIsNotTheLength()
    {
        SourceLengthReading reading = await Reading(standIns.Script("printf 'start_time=30499.474078\\n'"));

        Assert.False(reading.Measured);
        Assert.Equal(SourceLengthFault.SaidNothing, reading.Fault);
    }

    [Fact]
    public async Task ALengthFfprobeCouldNotWorkOutIsNoLength()
    {
        SourceLengthReading reading = await Reading(standIns.Script("printf 'duration=N/A\\n'"));

        Assert.False(reading.Measured);
        Assert.Equal(SourceLengthFault.SaidNothing, reading.Fault);
    }

    [Fact]
    public async Task ASourceOfNoLengthIsNoLength()
    {
        SourceLengthReading reading = await Reading(standIns.Script("printf 'duration=0.000000\\n'"));

        Assert.False(reading.Measured);
        Assert.Equal(SourceLengthFault.SaidNothing, reading.Fault);
    }

    [Fact]
    public async Task AProgrammeThatRefusedSaysWithWhatCode()
    {
        SourceLengthReading reading = await Reading(standIns.Script("printf 'no such file\\n' >&2; exit 1"));

        Assert.Equal(SourceLengthFault.Refused, reading.Fault);
        Assert.Equal(1, reading.ExitCode);
        Assert.Contains("no such file", reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProbeThatIsNotOnThisMachineIsNotAnExceptionToBeThrown()
    {
        SourceLengthReading reading = await Reading(standIns.Named("no-such-programme"));

        Assert.Equal(SourceLengthFault.ProgrammeMissing, reading.Fault);
        Assert.DoesNotContain('/', reading.Note);
    }

    [Fact]
    public async Task AProbeThatWillNotStopIsGivenUpOn()
    {
        SourceLengthReading reading = await Reading(
            standIns.Script("sleep 60"),
            TimeSpan.FromMilliseconds(250));

        Assert.Equal(SourceLengthFault.TimedOut, reading.Fault);
    }

    private Task<SourceLengthReading> Reading(string prober, TimeSpan? longest = null)
        => new FfprobeSourceLength(
                new MachineSettings { Prober = prober, LongestRead = longest ?? TimeSpan.FromSeconds(30) },
                TimeProvider.System)
            .ReadAsync(Source, CancellationToken.None);
}
