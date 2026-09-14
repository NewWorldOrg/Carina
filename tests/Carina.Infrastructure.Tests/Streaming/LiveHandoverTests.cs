using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveHandoverTests
{
    private static readonly LiveChannelKey Watched = new(new NetworkId(32736), new ServiceId(1024));

    private static readonly LiveSessionKey EveryFrame =
        new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private readonly HandTurnedClock clock = new();

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    private readonly SilentEvents events = new();

    private readonly LiveSessionManager manager;

    public LiveHandoverTests()
    {
        transcoders = new HeldTranscoders(budget);
        manager = new LiveSessionManager(
            new LiveSessionSettings(),
            new LiveFanoutSettings(),
            new LiveTranscodeSettings(),
            supply,
            transcoders,
            clock,
            events);
    }

    [Fact]
    public async Task TheChannelIsHandedOverAsItIsAndNoTranscoderIsRaised()
    {
        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        byte[] sent = [.. Enumerable.Range(0, 4_000).Select(at => (byte)(at % 251))];

        await supply.Opened[0].WriteAsync(sent);

        Assert.Equal(sent, await ReadAsync(carrying.Bytes, sent.Length));
        Assert.Equal(0, transcoders.Started);
        Assert.Equal(0, budget.Running);
    }

    [Fact]
    public async Task AReaderRidesTheReadingAViewerRaisedAndNoSecondTunerIsAskedFor()
    {
        LiveJoin joined = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.True(joined.Seated, joined.Note);

        await using ILiveViewing viewing = joined.Viewing!;

        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        Assert.Equal(1, supply.Asked);
        Assert.Single(supply.Opened);
        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task TheReadingIsLetGoOnceTheReaderHasStoppedAndNobodyElseIsBehindIt()
    {
        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await handed.Handed!.DisposeAsync();
        await Eventually.Happens(() => supply.Opened[0].Disposed, "the reading is let go");
    }

    [Fact]
    public async Task AChannelNoTunerWillReachIsRefusedAndNothingIsHandedOver()
    {
        supply.Refusing = LiveRefusal.NoTunerFree;

        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.Null(handed.Handed);
        Assert.Equal(LiveRefusal.NoTunerFree, handed.Refusal);
    }

    private static async Task<byte[]> ReadAsync(Stream reading, int many)
    {
        byte[] heard = new byte[many];
        int filled = 0;

        while (filled < many)
        {
            int read = await reading
                .ReadAsync(heard.AsMemory(filled), CancellationToken.None)
                .AsTask()
                .WaitAsync(Eventually.Patience);

            if (read is 0)
            {
                break;
            }

            filled += read;
        }

        return [.. heard.Take(filled)];
    }
}
