using System.Runtime.Versioning;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class FfprobeStreamAttributeReaderTests : IDisposable
{
    private static readonly StreamSource Source = new("/srv/recordings/k-1.ts");

    private readonly string room = Directory.CreateTempSubdirectory("carina-probe").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public async Task WhatTheProgrammePrintsIsWhatIsRead()
    {
        StreamAttributeReading reading = await Reading(Prints(Probes.Recorded(Probes.BroadcastHd)));

        Assert.True(reading.Measured);
        Assert.Equal(1440, reading.Attributes.Size.Width);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
        Assert.Equal(AudioMode.Stereo, reading.Attributes.Audio);
    }

    [Fact]
    public async Task TheArgumentsReachTheProgrammeOneByOne()
    {
        StreamAttributeReading reading = await Reading(Script("printf '%s\\n' \"$@\" | sed 's/^/arg=/'"));

        Assert.Equal(StreamProbeFault.SaidNothing, reading.Fault);

        StreamAttributeReading echoed = await Reading(
            Script("printf 'codec_type=video\\nwidth=%s\\nheight=1080\\nfield_order=tt\\nr_frame_rate=30/1\\n' \"$#\""));

        Assert.Equal(9, echoed.Attributes.Size.Width);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineIsSaidToBeMissing()
    {
        StreamAttributeReading reading = await Reading(Path.Combine(room, "no-such-programme"));

        Assert.Equal(StreamProbeFault.ProgrammeMissing, reading.Fault);
        Assert.False(reading.Measured);
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
        Assert.NotEmpty(reading.Note);
    }

    [Fact]
    public async Task AProgrammeThatRefusesIsReportedWithItsCodeAndItsComplaint()
    {
        StreamAttributeReading reading = await Reading(
            Script($"printf '%s' '{Probes.Recorded(Probes.Refused).Trim()}' >&2; exit 1"));

        Assert.Equal(StreamProbeFault.Refused, reading.Fault);
        Assert.Equal(1, reading.ExitCode);
        Assert.Contains("Invalid argument", reading.Note, StringComparison.Ordinal);
        Assert.Equal(4, reading.FellBackOn.Count);
    }

    [Fact]
    public async Task AProgrammeThatSaysNothingAtAllIsNotReadAsAnAnswer()
    {
        StreamAttributeReading reading = await Reading(Script("exit 0"));

        Assert.Equal(StreamProbeFault.SaidNothing, reading.Fault);
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
    }

    [Fact]
    public async Task AProgrammeThatNeverReturnsIsGivenUpOn()
    {
        StreamAttributeReading reading = await Reading(
            Script("sleep 60"),
            new StreamAttributeSettings { LongestRead = TimeSpan.FromMilliseconds(250) });

        Assert.Equal(StreamProbeFault.TimedOut, reading.Fault);
        Assert.False(reading.Measured);
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
    }

    [Fact]
    public async Task AReadThatIsCalledOffThrows()
    {
        var reader = new FfprobeStreamAttributeReader(
            new StreamAttributeSettings { Programme = Script("sleep 60") },
            TimeProvider.System);

        using var calledOff = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(Source, calledOff.Token));
    }

    [Fact]
    public async Task AnAnswerLongerThanThePipeHoldsIsStillReadWhole()
    {
        StreamAttributeReading reading = await Reading(
            Script("printf 'codec_type=video\\nwidth=1920\\nheight=1080\\nfield_order=progressive\\nr_frame_rate=30/1\\n'; "
                + "i=0; while [ $i -lt 20000 ]; do printf 'filler=%s\\n' \"$i\"; i=$((i+1)); done"));

        Assert.Equal(1920, reading.Attributes.Size.Width);
        Assert.Equal(ScanType.Progressive, reading.Attributes.Scan);
    }

    private Task<StreamAttributeReading> Reading(string programme, StreamAttributeSettings? settings = null)
    {
        var reader = new FfprobeStreamAttributeReader(
            (settings ?? new StreamAttributeSettings()) with { Programme = programme },
            TimeProvider.System);

        return reader.ReadAsync(Source, CancellationToken.None);
    }

    private string Prints(string output) => Script($"cat <<'ANSWER'\n{output}\nANSWER");

    private string Script(string body)
    {
        string path = Path.Combine(room, $"stand-in-{Guid.NewGuid():N}");

        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }
}
