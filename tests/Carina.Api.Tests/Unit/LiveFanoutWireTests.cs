using Carina.Api.Live;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class LiveFanoutWireTests
{
    private static readonly LiveWireSettings Impatient = new()
    {
        BetweenPings = TimeSpan.FromSeconds(30),
        WritePatience = TimeSpan.FromMilliseconds(200),
    };

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    [Fact]
    public async Task OneViewerHoldingItsSocketNeitherHoldsUpAnotherNorTheSource()
    {
        LiveFanout fanout = new(new LiveFanoutSettings { LongestBacklog = 3 });
        ScriptedWebSocket prompt = new();
        ScriptedWebSocket held = new() { HoldEverySend = TimeSpan.FromSeconds(30) };
        await using ILiveViewing promptViewing = await Joined(fanout);
        await using ILiveViewing heldViewing = await Joined(fanout);

        Task<LiveDeparture> promptCarrying = Carry(prompt, promptViewing);
        Task<LiveDeparture> heldCarrying = Carry(held, heldViewing);

        for (ulong pts = 0; pts < 20; pts++)
        {
            fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(pts), Picture));
            await Eventually.Happens(() => prompt.Sent.Count == (int)pts + 1, "the prompt socket takes the frame");
        }

        fanout.End();

        Assert.Equal(LiveDeparture.SourceEnded, await promptCarrying);
        Assert.Equal(LiveDeparture.ViewerStoppedReading, await heldCarrying);
        Assert.Equal(20, prompt.Sent.Count);
        Assert.Equal(LiveBacklog.Empty, promptViewing.Backlog);
        Assert.InRange(heldViewing.Backlog.Dropped, 16L, 17L);
        Assert.Empty(held.Sent);
        Assert.True(held.Aborted);
    }

    [Fact]
    public async Task TheHeaderReachesAViewerThatJoinedAfterItWasSentBeforeAnyPicture()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        ScriptedWebSocket socket = new();

        fanout.Publish(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(1UL), Picture));

        await using ILiveViewing viewing = await Joined(fanout);

        Task<LiveDeparture> carrying = Carry(socket, viewing);

        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(2UL), Picture));
        fanout.End();

        Assert.Equal(LiveDeparture.SourceEnded, await carrying);
        Assert.Equal([0x00, 0x01], socket.Sent.Select(message => message[0]).ToArray());
        Assert.Equal(2UL, LiveFrame.Read(socket.Sent[1]).Frame?.Pts.Value);
    }

    [Fact]
    public async Task TheSourceBreakingReachesEveryWireAsABreakRatherThanAnEnd()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        ScriptedWebSocket one = new();
        ScriptedWebSocket another = new();
        await using ILiveViewing oneViewing = await Joined(fanout);
        await using ILiveViewing anotherViewing = await Joined(fanout);

        Task<LiveDeparture> oneCarrying = Carry(one, oneViewing);
        Task<LiveDeparture> anotherCarrying = Carry(another, anotherViewing);

        fanout.Break(LiveFragmentFault.StoppedPartWayThrough);

        Assert.Equal(LiveDeparture.SourceBroke, await oneCarrying);
        Assert.Equal(LiveDeparture.SourceBroke, await anotherCarrying);
    }

    private static Task<LiveDeparture> Carry(ScriptedWebSocket socket, ILiveViewing viewing)
        => new LiveWireSocket(socket, Impatient)
            .CarryAsync(viewing.Frames, CancellationToken.None, CancellationToken.None);

    private static async Task<ILiveViewing> Joined(LiveFanout fanout)
    {
        ILiveViewing? viewing = await fanout.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);

        return viewing;
    }
}
