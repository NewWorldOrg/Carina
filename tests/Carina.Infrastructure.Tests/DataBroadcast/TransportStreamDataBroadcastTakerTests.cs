using System.Text;

using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class TransportStreamDataBroadcastTakerTests : IDisposable
{
    private const long Second = CarouselBroadcast.Second;

    private static readonly long Turn = (long)LivePts.ComesAroundAt;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(CarouselBroadcast.ProgramNumber);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml>1</bml>")));

    private static readonly CarouselModule StartupAgain = new(
        0x0000,
        2,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml>2</bml>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47]));

    private readonly string room = Directory.CreateTempSubdirectory("carina-data-broadcast-taker-").FullName;

    private readonly HeldClockStart starts = new();

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-005: every version of every module is taken from the start of the file to its end, each with when it was first seen, beside the event messages")]
    public async Task EveryVersionOfEveryModuleIsTakenWithWhenItWasFirstSeen()
    {
        starts.Start = TimeSpan.FromSeconds(0.5);

        DataBroadcastTaking taking = await Taker().TakeAsync(await WrittenAsync(Carrying(0)), Service, Cancel);

        DataBroadcastRecord record = taking.Record!;
        Assert.Null(taking.Fault);
        Assert.Equal((Second / 2, CarouselBroadcast.EntryTag, false, 2), (record.StartsAt, record.EntryTag, record.Incomplete, record.Modules));
        RecordedCarousel carousel = Assert.Single(record.Carousels);
        Assert.Equal(
            [(0, 1, Second), (1, 1, Second), (0, 2, 9 * Second)],
            carousel.Versions.Select(version => (version.ModuleId, version.Version, version.FirstSeen)));
        Assert.Equal(12 * Second, carousel.Versions[2].LastSeen);
        Assert.Equal("<bml>2</bml>", Encoding.UTF8.GetString(carousel.Versions[2].Resources[0].Body.Span));
        EventMessage message = Assert.Single(record.Events);
        Assert.Equal((0x12, 0x0345, 5 * Second), (message.Group, message.Id, message.FiresAt));
        Assert.Equal([Path.Combine(room, "recording.m2ts")], starts.Asked);
    }

    [Fact(DisplayName = "BR-BD-005: a file whose clock comes around and is said to begin before nothing is told a whole turn earlier, on the clock its captions are told on")]
    public async Task AFileWhoseClockComesAroundIsToldAWholeTurnEarlier()
    {
        starts.Start = TimeSpan.FromSeconds(-0.5);

        DataBroadcastRecord record = (await Taker().TakeAsync(await WrittenAsync(Carrying(Turn - (2 * Second))), Service, Cancel)).Record!;

        Assert.Equal(-Second / 2, record.StartsAt);
        Assert.Equal([-Second, -Second, 7 * Second], record.Carousels[0].Versions.Select(version => version.FirstSeen));
        Assert.Equal(3 * Second, Assert.Single(record.Events).FiresAt);
    }

    [Fact(DisplayName = "BR-BD-005: a file whose clock comes around after it begins keeps the clock followed through its wrap")]
    public async Task AFileWhoseClockComesAroundAfterItBeginsKeepsTheClockFollowedThroughItsWrap()
    {
        long first = Turn - (2 * Second);
        starts.Start = TimeSpan.FromTicks((first + (Second / 2)) * TimeSpan.TicksPerSecond / Second);

        DataBroadcastRecord record = (await Taker().TakeAsync(await WrittenAsync(Carrying(first)), Service, Cancel)).Record!;

        Assert.Equal(first + (Second / 2), record.StartsAt);
        Assert.Equal([Turn - Second, Turn - Second, Turn + (7 * Second)], record.Carousels[0].Versions.Select(version => version.FirstSeen));
        Assert.Equal(Turn + (3 * Second), Assert.Single(record.Events).FiresAt);
    }

    [Fact(DisplayName = "BR-BD-005: the record holds every module and event message live viewing sends of the same bytes, byte for byte")]
    public async Task TheRecordHoldsWhatLiveViewingSends()
    {
        byte[] broadcast = Carrying(0);
        starts.Start = TimeSpan.Zero;
        DataBroadcastSession session = new(Service, NullLogger.Instance);
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = (await fanout.JoinAsync(Cancel))!;
        session.Show(fanout);
        await session.Seat.WriteAsync(broadcast, Cancel);
        List<byte[]> live = [];

        while (viewing.Frames.TryRead(out LiveFrame? frame))
        {
            live.Add(frame.Payload.ToArray());
        }

        DataBroadcastRecord record = (await Taker().TakeAsync(await WrittenAsync(broadcast), Service, Cancel)).Record!;

        Assert.Equal(
            live.Where(payload => payload[0] == DataBroadcastFrames.ModuleKind),
            record.Carousels.SelectMany(carousel => carousel.Versions).Select(DataBroadcastFrames.ModulePayload));
        Assert.Equal(
            live.Where(payload => payload[0] == DataBroadcastFrames.EventKind),
            record.Events.Select(message => DataBroadcastFrames.Event(message, 0).Payload.ToArray()));
    }

    [Fact(DisplayName = "BR-BS-001: a recording whose service carries no data broadcast has none, and nothing is asked of its clock")]
    public async Task ARecordingWithNoDataBroadcastHasNone()
    {
        byte[] none = new CarouselBroadcast().Associated().Mapped(carrying: false).At(Second).At(2 * Second).Bytes;

        DataBroadcastTaking taking = await Taker().TakeAsync(await WrittenAsync(none), Service, Cancel);

        Assert.Equal((null, null, 0), (taking.Record, taking.Fault, taking.Modules));
        Assert.Empty(starts.Asked);
    }

    [Fact(DisplayName = "BR-BS-001: a recording whose carousel is listed and never delivered has no record either")]
    public async Task ACarouselListedAndNeverDeliveredIsNoRecord()
    {
        byte[] listed = new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).At(2 * Second).Bytes;

        DataBroadcastTaking taking = await Taker().TakeAsync(await WrittenAsync(listed), Service, Cancel);

        Assert.Equal((null, null), (taking.Record, taking.Fault));
    }

    [Fact(DisplayName = "BR-BS-001: a file with no clock at all, or none of the stream, has no record")]
    public async Task AFileWithNoClockHasNoRecord()
    {
        Assert.Null((await Taker().TakeAsync(await WrittenAsync(new CarouselBroadcast().Associated().Mapped().Bytes), Service, Cancel)).Record);
        Assert.Null((await Taker().TakeAsync(await WrittenAsync(new byte[4096]), Service, Cancel)).Record);
    }

    [Fact(DisplayName = "BR-BS-001: a record whose file's clock cannot be read is a failure that says why")]
    public async Task ARecordWhoseClockCannotBeReadIsAFailure()
    {
        starts.Note = "it exited 1";

        DataBroadcastTaking taking = await Taker().TakeAsync(await WrittenAsync(Carrying(0)), Service, Cancel);

        Assert.Equal((DataBroadcastFault.ClockUnread, "it exited 1"), (taking.Fault, taking.Note));
        Assert.Null(taking.Record);
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(90_000L, 0L)]
    [InlineData(-90_000L, 0L)]
    [InlineData(-(1L << 33) + 90_000, -(1L << 33))]
    [InlineData((1L << 33) - 90_000, 1L << 33)]
    public void TheClocksAreMovedByTheWholeTurnsNearestToHowFarApartTheyAre(long apart, long turns)
        => Assert.Equal(turns, TransportStreamDataBroadcastTaker.Turns(apart));

    private TransportStreamDataBroadcastTaker Taker()
        => new(starts, new CaptionSettings(), TimeProvider.System);

    private static byte[] Carrying(long from)
        => new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(from + Second)
            .Listed(1, Startup, Logo)
            .Delivered(Startup)
            .Delivered(Logo)
            .At(from + (5 * Second))
            .Fired(0, 0x12, 0x0345, 0x06, 0xEE)
            .At(from + (9 * Second))
            .Listed(2, StartupAgain, Logo)
            .Delivered(StartupAgain)
            .At(from + (12 * Second))
            .Bytes;

    private async Task<string> WrittenAsync(byte[] broadcast)
    {
        string written = Path.Combine(room, "recording.m2ts");
        await File.WriteAllBytesAsync(written, broadcast, Cancel);

        return written;
    }

    private sealed class HeldClockStart : IRecordingClockStart
    {
        public TimeSpan? Start { get; set; }

        public string Note { get; set; } = string.Empty;

        public List<string> Asked { get; } = [];

        public Task<RecordingClockStartReading> ReadAsync(string source, CancellationToken cancellationToken)
        {
            Asked.Add(source);

            return Task.FromResult(new RecordingClockStartReading(Start, Note));
        }
    }
}
