using System.Text;
using System.Threading.Channels;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class DataBroadcastSessionTests
{
    private const long Second = CarouselBroadcast.Second;

    private static readonly ServiceId Service = new(CarouselBroadcast.ProgramNumber);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml/>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47]));

    [Fact(DisplayName = "BR-BD-004: a viewer joining later is handed the latest catalog and then every module that has arrived")]
    public async Task AViewerJoiningLaterIsHandedTheCatalogAndThenEveryModule()
    {
        DataBroadcastSession session = Session();
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);

        session.Read(Carrying().Listed(1, Startup, Logo).Delivered(Startup).Delivered(Logo).Bytes);

        await using ILiveViewing late = await Joined(fanout);
        IReadOnlyList<LiveFrame> handed = Taken(late.Frames);

        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            handed.Select(SideChannelReading.KindOf));
        Assert.Equal([0, 1], handed.Skip(1).Select(frame => SideChannelReading.Module(frame).ModuleId));
        IReadOnlyDictionary<string, object> catalog = SideChannelReading.Catalog(handed[0]);
        Assert.Equal(2, ((List<object>)((Dictionary<string, object>)((List<object>)catalog["carousels"])[0])["modules"]).Count);
        Assert.Equal(session.Standing, fanout.Kept);
    }

    [Fact(DisplayName = "BR-BS-002: a module the catalog no longer lists is not handed to a viewer joining afterwards")]
    public async Task AModuleTheCatalogNoLongerListsIsNotHandedOn()
    {
        DataBroadcastSession session = Session();
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);
        CarouselBroadcast broadcast = Carrying().Listed(1, Startup, Logo).Delivered(Startup).Delivered(Logo);

        session.Read(broadcast.Bytes);
        session.Read(new CarouselBroadcast().At(2 * Second).Listed(2, Startup).Bytes);

        await using ILiveViewing late = await Joined(fanout);
        IReadOnlyList<LiveFrame> handed = Taken(late.Frames);

        Assert.Equal([DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind], handed.Select(SideChannelReading.KindOf));
        Assert.Equal(0, SideChannelReading.Module(handed[1]).ModuleId);
    }

    [Fact(DisplayName = "BR-BD-004: a service with no data broadcast hands every viewer, and one joining later, word that there is none")]
    public async Task AServiceWithNoDataBroadcastSaysSoToEveryViewer()
    {
        DataBroadcastSession session = Session();
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);
        await using ILiveViewing early = await Joined(fanout);

        session.Read(new CarouselBroadcast().Associated().Mapped(carrying: false).At(Second).Bytes);

        await using ILiveViewing late = await Joined(fanout);

        Assert.Equal([DataBroadcastFrames.AbsentKind], Taken(early.Frames).Select(SideChannelReading.KindOf));
        LiveFrame kept = Assert.Single(Taken(late.Frames));
        Assert.Equal([DataBroadcastFrames.AbsentKind], kept.Payload.ToArray());
        Assert.Equal((ulong)Second, kept.Pts.Value);
    }

    [Fact(DisplayName = "BR-BD-004: an event message reaches the viewers watching and is not handed to one joining later")]
    public async Task AnEventMessageReachesTheViewersWatchingAndIsNotKept()
    {
        DataBroadcastSession session = Session();
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);
        session.Read(Carrying().Bytes);
        await using ILiveViewing watching = await Joined(fanout);
        Taken(watching.Frames);

        session.Read(new CarouselBroadcast().At(2 * Second).Fired(0, 1, 2, 3).Bytes);

        await using ILiveViewing late = await Joined(fanout);

        EventRead fired = SideChannelReading.Event(Assert.Single(Taken(watching.Frames)));
        Assert.Equal((ulong)(2 * Second), fired.FiresAt);
        Assert.Equal([DataBroadcastFrames.CatalogKind], Taken(late.Frames).Select(SideChannelReading.KindOf));
    }

    [Fact(DisplayName = "BR-BD-004: the state goes on being read while nobody is shown it, and a fan-out shown it later starts from where it is")]
    public async Task TheStateGoesOnBeingReadWhileNobodyIsShownIt()
    {
        DataBroadcastSession session = Session();
        LiveFanout left = new(new LiveFanoutSettings());
        session.Show(left);
        session.Read(Carrying().Listed(1, Startup, Logo).Bytes);
        session.StopShowing(left);

        session.Read(new CarouselBroadcast().Delivered(Startup).Delivered(Logo).Bytes);

        LiveFanout shown = new(new LiveFanoutSettings());
        session.Show(shown);
        await using ILiveViewing viewing = await Joined(shown);

        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            Taken(viewing.Frames).Select(SideChannelReading.KindOf));
        Assert.Equal([DataBroadcastFrames.CatalogKind], left.Kept.Select(SideChannelReading.KindOf));
    }

    [Fact(DisplayName = "BR-BD-004: every fan-out of the channel is handed the same frames from the one state")]
    public void EveryFanoutOfTheChannelIsHandedTheSameFrames()
    {
        DataBroadcastSession session = Session();
        LiveFanout one = new(new LiveFanoutSettings());
        LiveFanout another = new(new LiveFanoutSettings());
        session.Show(one);
        session.Show(another);

        session.Read(Carrying().Listed(1, Startup).Delivered(Startup).Bytes);

        Assert.Equal(one.Kept, another.Kept);
        Assert.Equal(2, one.Kept.Count);
    }

    [Fact(DisplayName = "BR-BD-004: a fan-out shown the data broadcast hands its viewers what stands once, and keeps it once")]
    public async Task AFanoutShownTheDataBroadcastHandsWhatStandsOnce()
    {
        DataBroadcastSession session = Session();
        session.Read(Carrying().Listed(1, Startup, Logo).Delivered(Startup).Delivered(Logo).Bytes);
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing watching = await Joined(fanout);

        session.Show(fanout);

        Assert.Equal(session.Standing, Taken(watching.Frames));
        Assert.Equal(session.Standing, fanout.Kept);
        Assert.Equal(3, fanout.Kept.Count);
    }

    [Fact]
    public void WhatIsWrittenIntoTheSeatIsRead()
    {
        DataBroadcastSession session = Session();

        session.Seat.Write(Carrying().Bytes);

        Assert.Equal([DataBroadcastFrames.CatalogKind], session.Standing.Select(SideChannelReading.KindOf));
    }

    [Fact]
    public void TheSeatTakesNothingOnceItIsClosed()
    {
        DataBroadcastSession session = Session();

        session.Seat.Dispose();

        Assert.False(session.Seat.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => session.Seat.Write(Carrying().Bytes));
    }

    [Fact(DisplayName = "BR-BV-001: a failure while reading is written down with what failed, and nothing more is read")]
    public void AFailureWhileReadingIsWrittenDownAndNothingMoreIsRead()
    {
        ThrowingOnTheFirstWarning logger = new();
        DataBroadcastSession session = new(Service, logger);
        const long Largest = 16 * 1024 * 1024;

        session.Read(Carrying().Sections(
            CarouselBroadcast.CarouselPid,
            new DiiWriter { Modules = [.. Enumerable.Range(0, 5).Select(id => DiiModule.Of(id, Largest, 1))] }.ToSection().ToBytes()).Bytes);
        session.Read(new CarouselBroadcast().At(2 * Second).Listed(2, Startup).Delivered(Startup).Bytes);

        Exception written = Assert.Single(logger.Failures);
        Assert.IsType<InvalidOperationException>(written);
        Assert.Equal([DataBroadcastFrames.CatalogKind], session.Standing.Select(SideChannelReading.KindOf));
    }

    private static DataBroadcastSession Session() => new(Service, NullLogger.Instance);

    private static CarouselBroadcast Carrying() => new CarouselBroadcast().Associated().Mapped().At(Second);

    private static async Task<ILiveViewing> Joined(LiveFanout fanout)
    {
        ILiveViewing? viewing = await fanout.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);

        return viewing;
    }

    private static List<LiveFrame> Taken(ChannelReader<LiveFrame> frames)
    {
        List<LiveFrame> taken = [];

        while (frames.TryRead(out LiveFrame? frame))
        {
            taken.Add(frame);
        }

        return taken;
    }

    private sealed class ThrowingOnTheFirstWarning : ILogger
    {
        private bool thrown;

        public List<Exception> Failures { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            if (!thrown)
            {
                thrown = true;

                throw new InvalidOperationException("the first warning is refused for the test.");
            }

            if (exception is not null)
            {
                Failures.Add(exception);
            }
        }
    }
}
