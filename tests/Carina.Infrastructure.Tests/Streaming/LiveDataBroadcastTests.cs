using System.Text;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Streaming;

/// <summary>
/// The data broadcast of a live channel as the sessions of the channel carry it: read once from the one
/// reading of the tuner, and handed to every profile made from it.
/// </summary>
public sealed class LiveDataBroadcastTests : IAsyncDisposable
{
    private const long Second = CarouselBroadcast.Second;

    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(CarouselBroadcast.ProgramNumber), LiveProfile.Hd30);

    private static readonly LiveSessionKey EveryField = new(new NetworkId(32736), new ServiceId(CarouselBroadcast.ProgramNumber), LiveProfile.Hd60);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml/>")));

    private readonly PipedSupply supply = new();

    private readonly RefusingTheFirstCarouselLeftOut logger = new();

    private readonly LiveSessionManager manager;

    public LiveDataBroadcastTests()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

        manager = new LiveSessionManager(
            new LiveSessionSettings(linger: TimeSpan.FromMinutes(5)),
            new LiveFanoutSettings(),
            new LiveTranscodeSettings(),
            supply,
            new HeldTranscoders(budget),
            new HandTurnedClock(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero)),
            new SilentEvents(),
            logger);
    }

    public ValueTask DisposeAsync() => manager.DisposeAsync();

    [Fact(DisplayName = "BR-BD-004: two profiles of one channel are both handed the data broadcast read once from the one reading")]
    public async Task TwoProfilesOfOneChannelAreHandedTheOneDataBroadcast()
    {
        await using ILiveViewing frames = await Joined(EveryFrame);
        await using ILiveViewing fields = await Joined(EveryField);

        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Delivered(Startup).Bytes);

        LiveFrame[] toFrames = [await Next(frames), await Next(frames), await Next(frames), await Next(frames)];
        LiveFrame[] toFields = [await Next(fields), await Next(fields), await Next(fields), await Next(fields)];

        Assert.Single(supply.Opened);
        Assert.All(toFrames, frame => Assert.Equal(LiveChannel.DataBroadcast, frame.Channel));
        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.CatalogKind],
            toFrames.Select(SideChannelReading.KindOf));
        Assert.Equal(toFrames, toFields);
    }

    [Fact(DisplayName = "BR-BD-004: a viewer joining while the channel is watched is handed the catalog and every module before anything else")]
    public async Task AViewerJoiningLaterIsHandedTheCatalogAndEveryModuleFirst()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Delivered(Startup).Bytes);
        await Reached(first, DataBroadcastFrames.ModuleKind);

        await using ILiveViewing late = await Joined(EveryFrame);
        await using ILiveViewing anotherProfile = await Joined(EveryField);

        LiveFrame[] toTheLate = [await Next(late), await Next(late)];
        LiveFrame[] toAnotherProfile = [await Next(anotherProfile), await Next(anotherProfile)];

        Assert.Equal([DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind], toTheLate.Select(SideChannelReading.KindOf));
        Assert.Equal(toTheLate, toAnotherProfile);
    }

    [Fact(DisplayName = "BR-BD-004: a service with no data broadcast hands its viewers word that there is none")]
    public async Task AServiceWithNoDataBroadcastHandsItsViewersWordThatThereIsNone()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped(carrying: false).At(Second).Bytes);

        LiveFrame frame = await Next(viewing);
        Assert.Equal(LiveChannel.DataBroadcast, frame.Channel);
        Assert.Equal([DataBroadcastFrames.AbsentKind], frame.Payload.ToArray());
    }

    [Fact(DisplayName = "BR-BD-004: a data broadcast that stops reading tells its viewers there is none, and the next session of the channel reads it afresh")]
    public async Task ADataBroadcastThatStopsSaysSoAndTheNextSessionReadsItAfresh()
    {
        const long Largest = 16 * 1024 * 1024;
        await using ILiveViewing first = await Joined(EveryFrame);
        await supply.Opened[0].WriteAsync(new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(Second)
            .Sections(
                CarouselBroadcast.CarouselPid,
                new DiiWriter { Modules = [.. Enumerable.Range(0, 5).Select(id => DiiModule.Of(id, Largest, 1))] }.ToSection().ToBytes())
            .Bytes);

        await Reached(first, DataBroadcastFrames.AbsentKind);

        await using ILiveViewing next = await Joined(EveryField);
        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped(version: 1).At(2 * Second).Listed(1, Startup).Delivered(Startup).Bytes);

        LiveFrame[] afresh = [await Next(next), await Next(next), await Next(next), await Next(next)];

        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.CatalogKind],
            afresh.Select(SideChannelReading.KindOf));
        Assert.Single(supply.Opened);
        Assert.Equal(1, logger.Refused);
    }

    private async Task<ILiveViewing> Joined(LiveSessionKey key)
    {
        LiveJoin join = await manager.JoinAsync(key, CancellationToken.None);

        Assert.True(join.Seated, join.Note);

        return join.Viewing!;
    }

    private static async Task Reached(ILiveViewing viewing, byte kind)
    {
        while (SideChannelReading.KindOf(await Next(viewing)) != kind)
        {
        }
    }

    private static async Task<LiveFrame> Next(ILiveViewing viewing)
    {
        using CancellationTokenSource patience = new(Eventually.Patience);

        return await viewing.Frames.ReadAsync(patience.Token);
    }

    private sealed class RefusingTheFirstCarouselLeftOut : ILogger<LiveSessionManager>
    {
        private int refused;

        public int Refused => Volatile.Read(ref refused);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("was left out", StringComparison.Ordinal) && Interlocked.Exchange(ref refused, 1) is 0)
            {
                throw new InvalidOperationException("the first carousel left out is refused for the test.");
            }
        }
    }
}
