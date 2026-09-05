using System.Runtime.Versioning;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

[SupportedOSPlatform("linux")]
public sealed class MachineCapabilityReaderTests : IDisposable
{
    private const string Listing = """
        printf 'Encoders:\n V..... = Video\n ------\n V....D libx264   x264\n V....D h264_vaapi  vaapi\n V....D hevc_vaapi  vaapi\n'
        """;

    private const string CaptionListing = """
        printf 'Decoders:\n ------\n S..... libaribcaption   arib\n'
        """;

    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact(DisplayName = "BR-EV-004: what this machine can do is asked for once and the answer is kept")]
    public async Task WhatThisMachineCanDoIsAskedForOnceAndTheAnswerIsKept()
    {
        string counted = standIns.Named("counted");

        var reader = new MachineCapabilityReader(
            new MachineSettings
            {
                Programme = standIns.Script($"echo . >> {counted}; {Answering}"),
                RenderNode = standIns.Node(),
            },
            TimeProvider.System);

        MachineCapabilities[] answers = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => reader.ReadAsync(CancellationToken.None)));

        Assert.All(answers, answer => Assert.True(answer.CardIsUsable));
        Assert.Equal(4, File.ReadAllLines(counted).Length);
    }

    [Fact(DisplayName = "BR-EV-004: a card that encodes an H.264 frame and refuses an H.265 one is a usable card with H.264 on it alone")]
    public async Task ACardThatRefusesAnH265FrameIsAUsableCardWithH264Alone()
    {
        MachineCapabilities can = await Reading(
            standIns.Script($"""
                case "$*" in
                  *-encoders*) {Listing} ;;
                  *-decoders*) {CaptionListing} ;;
                  *hevc_vaapi*) printf 'No usable encoding entrypoint found for profile VAProfileHEVCMain (17).\n' >&2; exit 218 ;;
                  *) exit 0 ;;
                esac
                """),
            standIns.Node());

        Assert.Equal(CardStanding.Usable, can.Card);
        Assert.Equal(string.Empty, can.Note);
        Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.DecodeAribCaptions],
            can.Faculties);
    }

    [Fact(DisplayName = "BR-EV-004: a card that refuses H.264 is not asked about H.265 at all")]
    public async Task ACardThatRefusesH264IsNotAskedAboutH265()
    {
        string asked = standIns.Named("asked");

        MachineCapabilities can = await Reading(
            standIns.Script($"""
                echo "$*" >> {asked}
                case "$*" in
                  *-encoders*) {Listing} ;;
                  *-decoders*) {CaptionListing} ;;
                  *) exit 218 ;;
                esac
                """),
            standIns.Node());

        Assert.Equal(CardStanding.DriverUnusable, can.Card);
        Assert.Equal(3, File.ReadAllLines(asked).Length);
        Assert.DoesNotContain(File.ReadAllLines(asked), line => line.Contains("hevc_vaapi", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-EV-004: a machine with a card that works says it can encode on it")]
    public async Task AMachineWithACardThatWorksSaysItCanEncodeOnIt()
    {
        MachineCapabilities can = await Reading(standIns.Script(Answering), standIns.Node());

        Assert.Equal(CardStanding.Usable, can.Card);
        Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
            ],
            can.Faculties);
    }

    [Fact(DisplayName = "BR-EV-004: no render node is not an error, it is a machine that encodes on its processor")]
    public async Task NoRenderNodeIsNotAnErrorItIsAMachineThatEncodesOnItsProcessor()
    {
        MachineCapabilities can = await Reading(standIns.Script(Answering), standIns.Named("no-such-node"));

        Assert.Equal(CardStanding.NodeMissing, can.Card);
        Assert.Equal([Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions], can.Faculties);
        Assert.NotEmpty(can.Note);
    }

    [Fact]
    public async Task ANodeThatCannotBeOpenedIsNotANodeThatCanBeUsed()
    {
        MachineCapabilities can = await Reading(standIns.Script(Answering), standIns.Room);

        Assert.Equal(CardStanding.NodeUnreadable, can.Card);
        Assert.False(can.Has(Faculty.EncodeH264OnTheCard));
    }

    [Fact]
    public async Task ANodeThatIsThereWithNoDriverBehindItIsNotANodeThatCanBeUsed()
    {
        MachineCapabilities can = await Reading(
            standIns.Script($"""
                case "$*" in
                  *-encoders*) {Listing} ;;
                  *-decoders*) {CaptionListing} ;;
                  *) printf 'No VA display found for device /dev/dri/renderD128.\n' >&2; exit 234 ;;
                esac
                """),
            standIns.Node());

        Assert.Equal(CardStanding.DriverUnusable, can.Card);
        Assert.Contains("No VA display found", can.Note, StringComparison.Ordinal);
        Assert.DoesNotContain('/', can.Note);
        Assert.False(can.Has(Faculty.EncodeH264OnTheCard));
        Assert.True(can.Has(Faculty.EncodeH264OnTheProcessor));
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineLeavesItAbleToDoNothing()
    {
        MachineCapabilities can = await Reading(standIns.Named("no-such-programme"), standIns.Node());

        Assert.Equal(CardStanding.ProbeProgrammeMissing, can.Card);
        Assert.Empty(can.Faculties);
        Assert.NotEmpty(can.Note);
        Assert.DoesNotContain('/', can.Note);
    }

    [Fact]
    public async Task AskingTheCardIsGivenUpOnAndNothingIsLeftRunning()
    {
        string pids = standIns.Named("pids");

        MachineCapabilities can = await Reading(
            standIns.Script($"""
                case "$*" in
                  *-encoders*) {Listing} ;;
                  *-decoders*) {CaptionListing} ;;
                  *) echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait ;;
                esac
                """),
            standIns.Node(),
            TimeSpan.FromMilliseconds(250));

        Assert.Equal(CardStanding.ProbeTimedOut, can.Card);
        Assert.True(await standIns.NothingIsLeftOf(StandIns.Pids(pids)));
    }

    [Fact]
    public async Task ACallerThatStopsWaitingDoesNotStopTheAsking()
    {
        var reader = new MachineCapabilityReader(
            new MachineSettings
            {
                Programme = standIns.Script($"sleep 0.3; {Answering}"),
                RenderNode = standIns.Node(),
            },
            TimeProvider.System);

        using var calledOff = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(calledOff.Token));

        Assert.True((await reader.ReadAsync(CancellationToken.None)).CardIsUsable);
    }

    [Fact]
    public void TheNodeThatIsOpenedIsTheNodeTheCommandNames()
        => Assert.Contains(MachineSettings.TheRenderNode, VaapiProbeInvocation.Arguments(MachineSettings.TheRenderNode));

    private static string Answering => $"""
        case "$*" in
          *-encoders*) {Listing} ;;
          *-decoders*) {CaptionListing} ;;
          *) exit 0 ;;
        esac
        """;

    private static Task<MachineCapabilities> Reading(string programme, string renderNode, TimeSpan? longest = null)
        => new MachineCapabilityReader(
                new MachineSettings
                {
                    Programme = programme,
                    RenderNode = renderNode,
                    LongestProbe = longest ?? TimeSpan.FromSeconds(30),
                },
                TimeProvider.System)
            .ReadAsync(CancellationToken.None);
}
