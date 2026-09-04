using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Scanning;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class FfmpegThumbnailRendererTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact]
    public async Task APictureIsDrawnWhenTheProgrammeWritesOneAndSaysItIsDone()
    {
        ThumbnailRender render = await Renderer(Standing("""
            for argument in "$@"; do destination=$argument; done
            printf 'a picture' > "$destination"
            """)).RenderAsync(Request(), Cancel);

        Assert.True(render.Drew);
        Assert.Null(render.Fault);
        Assert.Equal("a picture", await File.ReadAllTextAsync(Destination(), Cancel));
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineSaysSoRatherThanBlamingTheRecording()
    {
        ThumbnailRender render = await Renderer(tree.Under("no-such-programme")).RenderAsync(Request(), Cancel);

        Assert.Equal(ThumbnailFault.ProgrammeMissing, render.Fault);
        Assert.Null(render.ExitCode);
    }

    [Fact]
    public async Task ARecordingThatIsNotWhereTheLedgerSaysIsToldApartFromTheProgrammeFailing()
    {
        string marker = tree.Under("it-ran");
        ThumbnailRender render = await Renderer(Standing($"printf ran > \"{marker}\""))
            .RenderAsync(
                new ThumbnailRequest(tree.Under("gone.m2ts"), Destination(), new ServiceId(1032), TimeSpan.FromSeconds(1)),
                Cancel);

        Assert.Equal(ThumbnailFault.SourceOutOfReach, render.Fault);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task AProgrammeThatRefusesCarriesTheCodeAndWhatItComplainedAbout()
    {
        ThumbnailRender render = await Renderer(Standing("""
            echo 'Invalid data found when processing input' >&2
            exit 234
            """)).RenderAsync(Request(), Cancel);

        Assert.Equal(ThumbnailFault.Refused, render.Fault);
        Assert.Equal(234, render.ExitCode);
        Assert.Equal("Invalid data found when processing input", render.Note);
    }

    [Fact]
    public async Task AProgrammeThatSaysItWorkedAndLeavesNothingBehindIsNotBelieved()
    {
        ThumbnailRender render = await Renderer(Standing("exit 0")).RenderAsync(Request(), Cancel);

        Assert.Equal(ThumbnailFault.NothingWasWritten, render.Fault);
        Assert.Null(render.ExitCode);
    }

    [Fact]
    public async Task AnEmptyPictureIsNothingWrittenTooBecauseNobodyCanLookAtIt()
    {
        ThumbnailRender render = await Renderer(Standing("""
            for argument in "$@"; do destination=$argument; done
            : > "$destination"
            """)).RenderAsync(Request(), Cancel);

        Assert.Equal(ThumbnailFault.NothingWasWritten, render.Fault);
    }

    [Fact]
    public async Task AProgrammeThatWillNotFinishIsGivenUpOnAndSaidToHaveTimedOut()
    {
        ThumbnailRender render = await Renderer(Standing("sleep 60"), new HurriedClock())
            .RenderAsync(Request(), Cancel);

        Assert.Equal(ThumbnailFault.TimedOut, render.Fault);
        Assert.Null(render.ExitCode);
    }

    [Fact]
    public async Task BeingAskedToStopIsNotTheSameAsRunningOutOfTime()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Renderer(Standing("sleep 60")).RenderAsync(Request(), stopping.Token));
    }

    [Fact]
    public async Task ThePictureGoesWhereItIsAskedForEvenWhenNothingIsThereYet()
    {
        string destination = tree.Under("pictures", "deeper", "one.jpg");

        ThumbnailRender render = await Renderer(Standing("""
            for argument in "$@"; do destination=$argument; done
            printf 'a picture' > "$destination"
            """)).RenderAsync(
                new ThumbnailRequest(Source(), destination, new ServiceId(1032), TimeSpan.FromSeconds(1)),
                Cancel);

        Assert.True(render.Drew);
        Assert.True(File.Exists(destination));
    }

    [Theory]
    [InlineData(null, "thumbnail=100,scale=960:trunc(960/dar/2)*2:flags=bicubic,setsar=1")]
    [InlineData(640, "thumbnail=100,scale=640:trunc(640/dar/2)*2:flags=bicubic,setsar=1")]
    public async Task TheWidthTheSettingsNameIsTheWidthTheProgrammeIsAskedFor(int? width, string expected)
    {
        string arguments = tree.Under("arguments");

        ThumbnailRender render = await Renderer(Dumping(arguments), width: width).RenderAsync(Request(), Cancel);

        Assert.True(render.Drew);
        Assert.Contains(expected, await File.ReadAllLinesAsync(arguments, Cancel));
    }

    [Fact]
    public async Task ThePositionTheRequestNamesIsThePositionTheProgrammeIsAskedFor()
    {
        string arguments = tree.Under("arguments");

        await Renderer(Dumping(arguments)).RenderAsync(
            new ThumbnailRequest(Source(), Destination(), new ServiceId(1032), TimeSpan.FromSeconds(90.5)),
            Cancel);

        string[] asked = await File.ReadAllLinesAsync(arguments, Cancel);

        Assert.Equal("90.5", asked[Array.IndexOf(asked, "-ss") + 1]);
        Assert.Equal(Source(), asked[Array.IndexOf(asked, "-i") + 1]);
        Assert.True(Array.IndexOf(asked, "-ss") < Array.IndexOf(asked, "-i"));
    }

    [Fact]
    public async Task TheProgrammeIsAskedForTheRecordedServicesVideoAndNotForWhateverItWouldPick()
    {
        string arguments = tree.Under("arguments");

        await Renderer(Dumping(arguments)).RenderAsync(
            new ThumbnailRequest(Source(), Destination(), new ServiceId(23610), TimeSpan.Zero),
            Cancel);

        string[] asked = await File.ReadAllLinesAsync(arguments, Cancel);

        Assert.Equal("p:23610:v:0", asked[Array.IndexOf(asked, "-map") + 1]);
    }

    [Fact]
    public async Task AFrameComesBackAsBytesRatherThanAsAFileOnDisk()
    {
        ThumbnailRender render = await Renderer(Standing("printf '\\377\\330jpeg'"))
            .FrameAsync(Frame(), Cancel);

        Assert.True(render.Drew);
        Assert.Equal([0xff, 0xd8, (byte)'j', (byte)'p', (byte)'e', (byte)'g'], render.Picture);
        Assert.False(Directory.Exists(tree.Under("pictures")));
    }

    [Fact]
    public async Task AProgrammeThatSaysItWorkedAndHandsOverNothingDrewNoFrame()
    {
        ThumbnailRender render = await Renderer(Standing("exit 0")).FrameAsync(Frame(), Cancel);

        Assert.Equal(ThumbnailFault.NothingWasWritten, render.Fault);
        Assert.Null(render.Picture);
    }

    [Fact]
    public async Task AProgrammeThatRefusesAFrameCarriesItsCode()
    {
        ThumbnailRender render = await Renderer(Standing("exit 234")).FrameAsync(Frame(), Cancel);

        Assert.Equal(ThumbnailFault.Refused, render.Fault);
        Assert.Equal(234, render.ExitCode);
        Assert.Null(render.Picture);
    }

    [Fact]
    public async Task AFrameOutOfARecordingThatIsNotThereIsToldApartFromTheProgrammeFailing()
    {
        ThumbnailRender render = await Renderer(Standing("exit 0"))
            .FrameAsync(
                new ThumbnailFrameRequest(tree.Under("gone.m2ts"), new ServiceId(1032), TimeSpan.Zero),
                Cancel);

        Assert.Equal(ThumbnailFault.SourceOutOfReach, render.Fault);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineDrawsNoFrameEither()
    {
        ThumbnailRender render = await Renderer(tree.Under("no-such-programme")).FrameAsync(Frame(), Cancel);

        Assert.Equal(ThumbnailFault.ProgrammeMissing, render.Fault);
    }

    [Fact]
    public async Task AProgrammeThatHangsOverAFrameIsGivenUpOn()
    {
        ThumbnailRender render = await Renderer(Standing("sleep 60"), new HurriedClock())
            .FrameAsync(Frame(), Cancel);

        Assert.Equal(ThumbnailFault.TimedOut, render.Fault);
        Assert.Null(render.Picture);
    }

    [Fact]
    public async Task BeingAskedToStopMidFrameIsNotTheSameAsRunningOutOfTime()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Renderer(Standing("sleep 60")).FrameAsync(Frame(), stopping.Token));
    }

    [Fact]
    public async Task NoFrameRequestMeansNothingIsRun()
        => Assert.Equal(
            "request",
            (await Assert.ThrowsAsync<ArgumentNullException>(
                () => Renderer(Standing("exit 0")).FrameAsync(null!, Cancel))).ParamName);

    [Fact]
    public async Task NoRequestMeansNothingIsRun()
        => Assert.Equal(
            "request",
            (await Assert.ThrowsAsync<ArgumentNullException>(
                () => Renderer(Standing("exit 0")).RenderAsync(null!, Cancel))).ParamName);

    private FfmpegThumbnailRenderer Renderer(string programme, TimeProvider? clock = null, int? width = null)
        => new(
            width is { } asked
                ? new ThumbnailSettings
                {
                    Programme = programme,
                    LongestRender = TimeSpan.FromMinutes(5),
                    Width = asked,
                }
                : new ThumbnailSettings
                {
                    Programme = programme,
                    LongestRender = TimeSpan.FromMinutes(5),
                },
            clock ?? TimeProvider.System);

    private string Dumping(string arguments)
        => Standing(
            "for argument in \"$@\"; do printf '%s\\n' \"$argument\" >> \"" + arguments + "\"; done\n"
            + "for argument in \"$@\"; do destination=$argument; done\n"
            + "printf 'a picture' > \"$destination\"");

    private ThumbnailFrameRequest Frame()
        => new(Source(), new ServiceId(1032), TimeSpan.FromSeconds(1));

    private ThumbnailRequest Request() => new(Source(), Destination(), new ServiceId(1032), TimeSpan.FromSeconds(1));

    private string Source()
    {
        string source = tree.Under("recording.m2ts");

        if (!File.Exists(source))
        {
            File.WriteAllText(source, "not really a transport stream");
        }

        return source;
    }

    private string Destination() => tree.Under("pictures", "one.jpg");

    private string Standing(string script)
    {
        string path = tree.Under($"programme-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
