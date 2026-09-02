using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class LiveCaptionerFactoryTests : IDisposable
{
    private const string Clock = "[Parsed_showinfo_1 @ 0x1] config in time_base: 1/90000, frame_rate: 0/1";

    private static readonly ServiceId Service = new(1040);

    private static readonly StreamAttributes Tiny = new(
        new VideoSize(2, 2),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact]
    public async Task TheCommandTheProgrammeIsHandedIsTheOneTheCanvasAndServiceAskFor()
    {
        string said = standIns.Named("arguments");

        await using ILiveCaptioner running = await Started(standIns.Script($"printf '%s\\n' \"$@\" > {said}; cat > /dev/null"), Interlaced);

        await running.Input.DisposeAsync();
        await running.Completion;

        Assert.Equal(
            [.. FfmpegCaptionInvocation.Arguments(Service, new VideoSize(1440, 1080)), .. FfmpegCaptionInvocation.Delivery()],
            File.ReadAllLines(said));
    }

    [Fact]
    public async Task ThePictureTheProgrammeDrawsComesOutAsACaptionAtItsStamp()
    {
        string stamps = standIns.Named("stamps");

        File.WriteAllText(stamps, Clock + "\n[Parsed_showinfo_1 @ 0x1] n:   0 pts:90000 pts_time:1 duration: 0 s:2x2 \n");

        await using ILiveCaptioner running = await Started(
            standIns.Script($"cat {stamps} >&2; printf '\\377\\377\\377\\377\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0'; cat > /dev/null"),
            Tiny);

        LiveFrame drawn = await Next(running);

        Assert.Equal(LiveChannel.Caption, drawn.Channel);
        Assert.Equal(90_000UL, drawn.Pts.Value);

        CaptionPicture? picture = LiveCaptions.PictureOf(drawn);

        Assert.NotNull(picture);
        Assert.Equal((0, 0, 1, 1), (picture.Left, picture.Top, picture.Width, picture.Height));
        Assert.Equal(3, PalettePngTests.Decoded.Of(picture.Png.ToArray()).ColourType);

        await running.Input.DisposeAsync();
        await running.Frames.Completion.WaitAsync(Eventually.Patience);
    }

    [Fact]
    public async Task TheCorrectionFromTheSettingsMovesTheStamp()
    {
        string stamps = standIns.Named("stamps");

        File.WriteAllText(stamps, Clock + "\n[Parsed_showinfo_1 @ 0x1] n:   0 pts:90000 pts_time:1 duration: 0 s:2x2 \n");

        await using ILiveCaptioner running = await Started(
            standIns.Script($"cat {stamps} >&2; printf '\\377\\377\\377\\377\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0\\0'; cat > /dev/null"),
            Tiny,
            new LiveCaptionSettings { EncoderDelay = TimeSpan.FromMilliseconds(500) });

        Assert.Equal(135_000UL, (await Next(running)).Pts.Value);

        await running.Input.DisposeAsync();
    }

    [Fact]
    public async Task WhatIsWrittenInReachesTheProgramme()
    {
        string heard = standIns.Named("heard");

        await using ILiveCaptioner running = await Started(standIns.Script($"cat > {heard}"), Tiny);

        await running.Input.WriteAsync(new byte[] { 1, 2, 3 });
        await running.Input.FlushAsync();
        await running.Input.DisposeAsync();
        await running.Completion;

        Assert.Equal([1, 2, 3], File.ReadAllBytes(heard));
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineIsSaidToBeMissingAndNamesNoPath()
    {
        LiveCaptionerStart start = await Starting(standIns.Named("no-such-programme"), Tiny);

        Assert.False(start.Running);
        Assert.Equal(TranscoderFault.ProgrammeMissing, start.Fault);
        Assert.DoesNotContain('/', start.Note);
    }

    [Fact]
    public async Task WhatTheProgrammeComplainedOfIsKeptWithItsCodeAndTheStampsAreNotComplaint()
    {
        await using ILiveCaptioner running = await Started(
            standIns.Script($"printf '%s\\n' '{Clock}' 'Stream specifier matches no streams' >&2; exit 1"),
            Tiny);

        TranscoderExit ended = await running.Completion;

        Assert.False(ended.RanToTheEnd);
        Assert.Equal(1, ended.ExitCode);
        Assert.Contains("matches no streams", ended.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("showinfo", ended.Note, StringComparison.Ordinal);
        await running.Frames.Completion.WaitAsync(Eventually.Patience);
    }

    [Fact]
    public async Task ACaptionerThatIsCalledOffLeavesNothingRunningAndCompletesItsFrames()
    {
        string pids = standIns.Named("pids");

        using var callingOff = new CancellationTokenSource();

        ILiveCaptioner running = await Started(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            Tiny,
            cancellationToken: callingOff.Token);

        await WaitFor(pids, 2);
        await callingOff.CancelAsync();

        TranscoderExit ended = await running.Completion;
        await running.DisposeAsync();

        Assert.Equal(TranscoderFault.CalledOff, ended.Fault);
        Assert.True(running.Frames.Completion.IsCompleted);
        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task ACaptionerThatWillNotStopWhenTheInputEndsIsStoppedAnyway()
    {
        string pids = standIns.Named("pids");

        ILiveCaptioner running = await Started(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            Tiny,
            grace: TimeSpan.FromMilliseconds(250));

        await WaitFor(pids, 2);
        await running.DisposeAsync();

        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task ACaptionerThatEndedOnItsOwnIsDisposedOfWithoutComplaint()
    {
        ILiveCaptioner running = await Started(standIns.Script("exit 0"), Tiny);

        Assert.True((await running.Completion).RanToTheEnd);

        await running.DisposeAsync();
        await running.DisposeAsync();
    }

    [Fact]
    public void ACaptionerTakesNoPlaceInTheTranscodingBudget()
    {
        ConstructorInfo made = Assert.Single(typeof(LiveCaptionerFactory).GetConstructors());

        Assert.DoesNotContain(typeof(ITranscodeBudget), made.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            "Budget",
            typeof(LiveCaptionerFactory).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(field => field.FieldType.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<LiveFrame> Next(ILiveCaptioner running)
    {
        using CancellationTokenSource patience = new(Eventually.Patience);

        return await running.Frames.ReadAsync(patience.Token);
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

    private async Task<ILiveCaptioner> Started(
        string programme,
        StreamAttributes attributes,
        LiveCaptionSettings? captions = null,
        CancellationToken cancellationToken = default,
        TimeSpan? grace = null)
    {
        LiveCaptionerStart start = await Starting(programme, attributes, captions, cancellationToken, grace);

        Assert.True(start.Running, start.Note);

        return start.Captioner!;
    }

    private static Task<LiveCaptionerStart> Starting(
        string programme,
        StreamAttributes attributes,
        LiveCaptionSettings? captions = null,
        CancellationToken cancellationToken = default,
        TimeSpan? grace = null)
    {
        var settings = new LiveTranscodeSettings
        {
            Programme = programme,
            StopGrace = grace ?? TimeSpan.FromSeconds(2),
        };

        var factory = new LiveCaptionerFactory(settings, captions ?? new LiveCaptionSettings(), TimeProvider.System);

        return factory.StartAsync(Service, attributes, cancellationToken);
    }
}
