using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class LiveEncoderSelectionTests : IDisposable
{
    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact]
    public async Task TheCardIsUsedWhenItIsThere()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings { Prefer = LiveEncoder.Vaapi, Programme = standIns.Script("exit 0") },
            standIns.Node());

        Assert.Equal(LiveEncoder.Vaapi, chosen.Encoder);
        Assert.False(chosen.FellBack);
    }

    [Fact]
    public async Task NoNodeMeansSoftware()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings { Prefer = LiveEncoder.Vaapi, Programme = standIns.Script("exit 0") },
            standIns.Named("no-such-node"));

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(EncoderRefusal.NodeMissing, chosen.FellBackBecause);
        Assert.NotEmpty(chosen.Note);
    }

    [Fact]
    public async Task ANodeThatCannotBeOpenedIsNotANodeThatCanBeUsed()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings { Prefer = LiveEncoder.Vaapi, Programme = standIns.Script("exit 0") },
            standIns.Room);

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(EncoderRefusal.NodeUnreadable, chosen.FellBackBecause);
    }

    [Fact]
    public async Task ANodeThatIsThereWithNoDriverBehindItIsNotANodeThatCanBeUsed()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings
            {
                Prefer = LiveEncoder.Vaapi,
                Programme = standIns.Script("printf '%s\\n' 'Failed to initialise VAAPI connection: -1' >&2; exit 234"),
            },
            standIns.Node());

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(EncoderRefusal.DriverUnusable, chosen.FellBackBecause);
        Assert.Contains("Failed to initialise VAAPI connection", chosen.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatTheCardComplainedOfNamesNoPathOnThisMachine()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings
            {
                Prefer = LiveEncoder.Vaapi,
                Programme = standIns.Script("printf '%s\\n' 'No VA display found for device /dev/dri/renderD128.' >&2; exit 234"),
            },
            standIns.Node());

        Assert.Equal(EncoderRefusal.DriverUnusable, chosen.FellBackBecause);
        Assert.DoesNotContain('/', chosen.Note);
        Assert.Contains("No VA display found", chosen.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineMeansSoftware()
    {
        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings { Prefer = LiveEncoder.Vaapi, Programme = standIns.Named("no-such-programme") },
            standIns.Node());

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(EncoderRefusal.ProbeProgrammeMissing, chosen.FellBackBecause);
        Assert.DoesNotContain('/', chosen.Note);
    }

    [Fact]
    public async Task AskingTheCardIsGivenUpOnAndNothingIsLeftRunning()
    {
        string pids = standIns.Named("asked-pids");

        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings
            {
                Prefer = LiveEncoder.Vaapi,
                LongestProbe = TimeSpan.FromMilliseconds(250),
                Programme = standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            },
            standIns.Node());

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.Equal(EncoderRefusal.ProbeTimedOut, chosen.FellBackBecause);
        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task SoftwareIsWhatIsAskedForUntilSomebodyAsksForTheCard()
    {
        string ran = standIns.Named("ran");

        LiveEncoderChoice chosen = await Choosing(
            new LiveTranscodeSettings { Programme = standIns.Script($"touch {ran}") },
            standIns.Node());

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.False(chosen.FellBack);
        Assert.False(File.Exists(ran));
    }

    [Fact]
    public async Task TheCardIsAskedAboutOnceAndTheAnswerIsKept()
    {
        string counted = standIns.Named("counted");

        var selection = new LiveEncoderSelection(
            new LiveTranscodeSettings
            {
                Prefer = LiveEncoder.Vaapi,
                Programme = standIns.Script($"echo . >> {counted}; exit 0"),
            },
            TimeProvider.System,
            standIns.Node());

        LiveEncoderChoice[] answers = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => selection.ChooseAsync(CancellationToken.None)));

        Assert.All(answers, answer => Assert.Equal(LiveEncoder.Vaapi, answer.Encoder));
        Assert.Single(File.ReadAllLines(counted));
    }

    [Fact]
    public async Task ACallerThatStopsWaitingDoesNotStopTheAsking()
    {
        var selection = new LiveEncoderSelection(
            new LiveTranscodeSettings
            {
                Prefer = LiveEncoder.Vaapi,
                Programme = standIns.Script("sleep 0.5; exit 0"),
            },
            TimeProvider.System,
            standIns.Node());

        using var calledOff = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => selection.ChooseAsync(calledOff.Token));

        Assert.Equal(LiveEncoder.Vaapi, (await selection.ChooseAsync(CancellationToken.None)).Encoder);
    }

    [Fact]
    public void TheNodeThatIsOpenedIsTheNodeTheCommandNames()
    {
        ParameterInfo node = typeof(LiveEncoderSelection)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "renderNode", StringComparison.Ordinal));

        Assert.Equal(FfmpegLiveInvocation.RenderNode, node.DefaultValue);
        Assert.Contains(FfmpegLiveInvocation.RenderNode, VaapiProbeInvocation.Arguments());
    }

    private static IEnumerable<int> Read(string pids)
        => File.ReadAllLines(pids).Where(line => line.Length > 0).Select(line => int.Parse(line, CultureInfo.InvariantCulture));

    private static Task<LiveEncoderChoice> Choosing(LiveTranscodeSettings settings, string renderNode)
        => new LiveEncoderSelection(settings, TimeProvider.System, renderNode).ChooseAsync(CancellationToken.None);
}
