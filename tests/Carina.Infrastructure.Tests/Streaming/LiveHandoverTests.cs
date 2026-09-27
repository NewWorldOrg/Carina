using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveHandoverTests
{
    private const int MouthfulsThatOverfillTheReader = 128;

    private static readonly LiveChannelKey Watched = new(new NetworkId(32736), new ServiceId(1024));

    private static readonly LiveSessionKey EveryFrame =
        new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private static readonly TimeSpan SoonerThanASeatIsCutLooseFor = TimeSpan.FromSeconds(5);

    private static readonly LiveSessionSettings Settings = new();

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
            Settings,
            new LiveFanoutSettings(),
            new LiveTranscodeSettings(),
            supply,
            transcoders,
            clock,
            events,
            NullLogger<LiveSessionManager>.Instance);
    }

    [Fact]
    public async Task TheChannelIsHandedOverAsItIsAndNoTranscoderIsRaised()
    {
        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        byte[] sent = Mouthful();

        await supply.Opened[0].WriteAsync(sent);

        Assert.Equal(sent, await ReadAsync(carrying.Bytes, sent.Length));
        Assert.Equal(0, transcoders.Started);
        Assert.Equal(0, budget.Running);
    }

    [Fact]
    public async Task AReaderRidesTheReadingAViewerRaisedAndNoSecondTunerIsAskedFor()
    {
        LiveJoin joined = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.True(joined.Seated, "a viewer is seated on the channel");

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

    [Fact]
    public async Task ASeatThatRefusesAMouthfulInAWayNobodyNamedIsDroppedAndTheOthersGoOnBeingFed()
    {
        LiveJoin joined = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.True(joined.Seated, "a viewer is seated on the channel");

        await using ILiveViewing viewing = joined.Viewing!;

        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        transcoders.Raised[0].FailingToTake =
            new InvalidOperationException("writing to a writer that has already been completed.");

        byte[] sent = Mouthful();

        await supply.Opened[0].WriteAsync(sent);

        Assert.Equal(sent, await ReadAsync(carrying.Bytes, sent.Length));

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task AReaderThatHasLetGoIsNotOfferedAnotherMouthfulAndTheReadingCarriesOn()
    {
        LiveJoin joined = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.True(joined.Seated, "a viewer is seated on the channel");

        await using ILiveViewing viewing = joined.Viewing!;

        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        ILiveHandedOver carrying = handed.Handed!;

        await carrying.DisposeAsync();

        byte[] sent = Mouthful();

        await supply.Opened[0].WriteAsync(sent);
        await Eventually.Happens(
            () => transcoders.Raised[0].TakenIn >= sent.Length,
            "the transcoding seat is fed after the reader has let go");

        Assert.Equal(0, carrying.ChunksDroppedSinceTheSupplyOpened);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task AReaderThatNeverReadsDropsWhatItCannotHoldAndNeverHoldsUpTheTranscodingSeat()
    {
        LiveJoin joined = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.True(joined.Seated, "a viewer is seated on the channel");

        await using ILiveViewing viewing = joined.Viewing!;

        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        byte[] sent = new byte[64 * 1024];
        long poured = (long)sent.Length * MouthfulsThatOverfillTheReader;
        Task pouring = Task.Run(async () =>
        {
            for (int at = 0; at < MouthfulsThatOverfillTheReader; at++)
            {
                await supply.Opened[0].WriteAsync(sent);
            }
        });

        await Sooner(
            () => transcoders.Raised[0].TakenIn >= poured,
            "the transcoding seat is fed everything while the reader takes nothing");
        await pouring.WaitAsync(Eventually.Patience);

        Assert.True(
            carrying.ChunksDroppedSinceTheSupplyOpened > 0,
            "a reader that takes nothing has what it cannot hold dropped and counted");
        Assert.NotEmpty(await ReadAsync(carrying.Bytes, sent.Length));

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task NothingIsSaidToHaveReachedTheReaderUntilTheFirstMouthfulHas()
    {
        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        Task<bool> reaching = carrying.ReachedAsync(CancellationToken.None).AsTask();

        Assert.False(reaching.IsCompleted, "nothing has come from the channel yet");

        await supply.Opened[0].WriteAsync(Mouthful());

        Assert.True(await reaching.WaitAsync(Eventually.Patience));
    }

    [Fact]
    public async Task AChannelThatSaysNothingWithinTheLongestRaiseNeverReachesTheReader()
    {
        LiveHandover handed = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(handed.Handed);

        await using ILiveHandedOver carrying = handed.Handed!;

        Task<bool> reaching = carrying.ReachedAsync(CancellationToken.None).AsTask();

        clock.Turn(Settings.LongestRaise);

        Assert.False(await reaching.WaitAsync(Eventually.Patience));
    }

    [Fact]
    public async Task AReaderIsNeverSeatedOnAReadingWhoseCarryingHasAlreadyEnded()
    {
        LiveHandover first = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.NotNull(first.Handed);

        supply.Opened[0].NoMore();

        await Eventually.Happens(
            () => supply.Opened[0].Disposed,
            "the reading lets the supply go once its carrying has ended");

        LiveHandover second = await manager.HandOverAsync(Watched, CancellationToken.None);

        Assert.Equal(2, supply.Asked);

        await first.Handed!.DisposeAsync();

        if (second.Handed is { } more)
        {
            await more.DisposeAsync();
        }
    }

    private static byte[] Mouthful() => [.. Enumerable.Range(0, 4_000).Select(at => (byte)(at % 251))];

    private static async Task Sooner(Func<bool> condition, string what)
    {
        long start = Environment.TickCount64;

        while (Environment.TickCount64 - start < SoonerThanASeatIsCutLooseFor.TotalMilliseconds)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        throw new TimeoutException($"Did not happen within {SoonerThanASeatIsCutLooseFor.TotalSeconds}s: {what}.");
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
