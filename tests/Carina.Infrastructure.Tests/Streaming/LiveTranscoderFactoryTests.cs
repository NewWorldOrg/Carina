using System.Globalization;
using System.Runtime.Versioning;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class LiveTranscoderFactoryTests : IDisposable
{
    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private static readonly ServiceId Service = new(1040);

    private readonly StandIns standIns = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 2 });

    public void Dispose() => standIns.Dispose();

    [Fact]
    public async Task TheCommandTheProgrammeIsHandedIsTheOneTheProfileAsksFor()
    {
        string said = standIns.Named("arguments");

        await using ILiveTranscoder running = await Started(
            standIns.Script($"printf '%s\\n' \"$@\" > {said}; cat > /dev/null"),
            LiveEncoder.Software);

        await running.Input.DisposeAsync();
        await running.Completion;

        string[] handed = File.ReadAllLines(said);

        Assert.Equal(
            [.. FfmpegLiveInvocation.Arguments(Service, LiveProfile.Hd30, Interlaced, LiveEncoder.Software), .. FfmpegLiveInvocation.Delivery()],
            handed);
    }

    [Fact]
    public async Task TheCommandNamesTheServiceWhosePictureAndSoundsItIsToTake()
    {
        string said = standIns.Named("arguments");

        await using ILiveTranscoder running = await Started(
            standIns.Script($"printf '%s\\n' \"$@\" > {said}; cat > /dev/null"),
            LiveEncoder.Software);

        await running.Input.DisposeAsync();
        await running.Completion;

        string[] handed = File.ReadAllLines(said);

        Assert.Contains("p:1040:v:0", handed);
        Assert.Contains("p:1040:a", handed);
    }

    [Fact]
    public async Task TheEncoderThatWasChosenIsTheEncoderTheCommandNames()
    {
        string said = standIns.Named("arguments");

        await using ILiveTranscoder running = await Started(
            standIns.Script($"printf '%s\\n' \"$@\" > {said}; cat > /dev/null"),
            LiveEncoder.Vaapi);

        await running.Input.DisposeAsync();
        await running.Completion;

        Assert.Equal(LiveEncoder.Vaapi, running.Encoder.Encoder);
        Assert.Contains("h264_vaapi", File.ReadAllLines(said));
    }

    [Fact]
    public async Task WhatIsWrittenInComesOutAgain()
    {
        await using ILiveTranscoder running = await Started(standIns.Script("cat"), LiveEncoder.Software);

        byte[] sent = Enumerable.Range(0, 188 * 100).Select(at => (byte)(at % 251)).ToArray();

        Task writing = WriteAndClose(running, sent);
        byte[] received = await ReadAll(running.Output);

        await writing;

        Assert.Equal(sent, received);
        Assert.True((await running.Completion).RanToTheEnd);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineIsSaidToBeMissingAndNamesNoPath()
    {
        LiveTranscoderStart start = await Starting(standIns.Named("no-such-programme"), LiveEncoder.Software);

        Assert.False(start.Running);
        Assert.Equal(TranscoderFault.ProgrammeMissing, start.Fault);
        Assert.DoesNotContain('/', start.Note);
    }

    [Fact]
    public async Task WhatTheProgrammeComplainedOfIsKeptAndReadBackWithItsCode()
    {
        await using ILiveTranscoder running = await Started(
            standIns.Script("printf '%s\\n' 'Invalid data found when processing input' >&2; exit 183"),
            LiveEncoder.Software);

        TranscoderExit ended = await running.Completion;

        Assert.False(ended.RanToTheEnd);
        Assert.Equal(TranscoderFault.Refused, ended.Fault);
        Assert.Equal(183, ended.ExitCode);
        Assert.Contains("Invalid data found", ended.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatTheProgrammeComplainedOfNamesNoPathOnThisMachine()
    {
        await using ILiveTranscoder running = await Started(
            standIns.Script("printf '%s\\n' 'Error opening output /srv/recordings/k-1.ts: No such file' >&2; exit 1"),
            LiveEncoder.Software);

        TranscoderExit ended = await running.Completion;

        Assert.DoesNotContain('/', ended.Note);
        Assert.Contains("Error opening output", ended.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgrammeThatNeverStopsComplainingDoesNotFillThisProcessUp()
    {
        await using ILiveTranscoder running = await Started(
            standIns.Script("i=0; while [ $i -lt 5000 ]; do printf 'line %s\\n' \"$i\" >&2; i=$((i+1)); done; exit 1"),
            LiveEncoder.Software);

        TranscoderExit ended = await running.Completion;

        Assert.True(ended.Note.Length <= TranscoderNote.Longest);
        Assert.Contains("line 4999", ended.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("line 0\n", ended.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATranscoderThatIsCalledOffLeavesNothingRunning()
    {
        string pids = standIns.Named("pids");

        using var callingOff = new CancellationTokenSource();

        ILiveTranscoder running = await Started(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            LiveEncoder.Software,
            callingOff.Token);

        await WaitFor(pids, 2);
        await callingOff.CancelAsync();

        TranscoderExit ended = await running.Completion;
        await running.DisposeAsync();

        Assert.Equal(TranscoderFault.CalledOff, ended.Fault);
        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task ATranscoderThatWillNotStopWhenTheInputEndsIsStoppedAnyway()
    {
        string pids = standIns.Named("pids");

        ILiveTranscoder running = await Started(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            LiveEncoder.Software,
            grace: TimeSpan.FromMilliseconds(250));

        await WaitFor(pids, 2);
        await running.DisposeAsync();

        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task ATranscoderThatEndedOnItsOwnIsDisposedOfWithoutComplaint()
    {
        ILiveTranscoder running = await Started(standIns.Script("exit 0"), LiveEncoder.Software);

        Assert.True((await running.Completion).RanToTheEnd);

        await running.DisposeAsync();
        await running.DisposeAsync();

        Assert.Equal(0, budget.Running);
    }

    [Fact]
    public async Task ATranscoderTakesAPlaceInTheBudgetForAsLongAsItRuns()
    {
        await using ILiveTranscoder first = await Started(standIns.Script("cat > /dev/null"), LiveEncoder.Software);
        ILiveTranscoder second = await Started(standIns.Script("cat > /dev/null"), LiveEncoder.Software);

        Assert.Equal(2, budget.Running);

        LiveTranscoderStart third = await Starting(standIns.Script("cat > /dev/null"), LiveEncoder.Software);

        Assert.False(third.Running);
        Assert.Equal(TranscoderFault.TooManyAlready, third.Fault);
        Assert.Equal(2, third.Ceiling!.Running);
        Assert.Equal(2, third.Ceiling.AtOnce);
        Assert.Contains("2 transcoder", third.Note, StringComparison.Ordinal);

        await second.Input.DisposeAsync();
        await second.Completion;

        Assert.Equal(2, budget.Running);

        await second.DisposeAsync();

        Assert.Equal(1, budget.Running);

        await using ILiveTranscoder next = await Started(standIns.Script("cat > /dev/null"), LiveEncoder.Software);

        Assert.Equal(2, budget.Running);
    }

    [Fact]
    public async Task ARecordingBeingPlayedTakesThePlaceALivePictureWould()
    {
        using ITranscodeSeat playing = budget.Claim(TranscodePurpose.Playback).Seat!;
        await using ILiveTranscoder live = await Started(standIns.Script("cat > /dev/null"), LiveEncoder.Software);

        LiveTranscoderStart refused = await Starting(standIns.Script("cat > /dev/null"), LiveEncoder.Software);

        Assert.Equal(TranscoderFault.TooManyAlready, refused.Fault);
        Assert.Equal(2, budget.Running);
    }

    [Fact]
    public async Task AProgrammeThatWouldNotStartHandsItsPlaceStraightBack()
    {
        LiveTranscoderStart start = await Starting(standIns.Named("no-such-programme"), LiveEncoder.Software);

        Assert.False(start.Running);
        Assert.Null(start.Ceiling);
        Assert.Equal(0, budget.Running);
    }

    private static async Task WaitFor(string pids, int howMany)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(pids) && Read(pids).Count() >= howMany)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"the stand-in never wrote {howMany} process identifiers");
    }

    private static IEnumerable<int> Read(string pids)
    {
        try
        {
            return
            [
                .. File.ReadAllLines(pids)
                    .Where(line => line.Length > 0)
                    .Select(line => int.Parse(line, CultureInfo.InvariantCulture)),
            ];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static async Task WriteAndClose(ILiveTranscoder running, byte[] sent)
    {
        await running.Input.WriteAsync(sent);
        await running.Input.FlushAsync();
        await running.Input.DisposeAsync();
    }

    private static async Task<byte[]> ReadAll(Stream reading)
    {
        using var held = new MemoryStream();

        await reading.CopyToAsync(held);

        return held.ToArray();
    }

    private async Task<ILiveTranscoder> Started(
        string programme,
        LiveEncoder encoder,
        CancellationToken cancellationToken = default,
        TimeSpan? grace = null)
    {
        LiveTranscoderStart start = await Starting(programme, encoder, cancellationToken, grace);

        Assert.True(start.Running, start.Note);

        return start.Transcoder!;
    }

    private Task<LiveTranscoderStart> Starting(
        string programme,
        LiveEncoder encoder,
        CancellationToken cancellationToken = default,
        TimeSpan? grace = null)
    {
        var settings = new LiveTranscodeSettings
        {
            Programme = programme,
            StopGrace = grace ?? TimeSpan.FromSeconds(2),
        };

        var factory = new LiveTranscoderFactory(settings, budget, new AlreadyChosen(encoder), TimeProvider.System);

        return factory.StartAsync(Service, LiveProfile.Hd30, Interlaced, cancellationToken);
    }

    private sealed class AlreadyChosen(LiveEncoder encoder) : ILiveEncoderSelector
    {
        public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
            => Task.FromResult(LiveEncoderChoice.Asked(encoder));
    }
}
