using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.DataBroadcast;

/// <summary>
/// Takes the record of the data broadcast out of a synthetic broadcast ffmpeg wrote, with a carousel put into it,
/// and holds it against the frames live viewing makes of the same bytes.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class TransportStreamDataBroadcastTakerMaterialTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly MachineSettings Machine = new();

    private readonly string room = Directory.CreateTempSubdirectory("carina-recorded-data-broadcast").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-005: the record taken from a broadcast ffmpeg wrote holds the modules and event messages live viewing sends of it, byte for byte")]
    public async Task TheRecordHoldsWhatLiveViewingSendsByteForByte()
    {
        string recorded = await CarryingACarouselAsync(TimeSpan.FromHours(13));
        IReadOnlyList<LiveFrame> live = await LiveAsync(await File.ReadAllBytesAsync(recorded, Cancel));

        DataBroadcastTaking taking = await Taker().TakeAsync(recorded, Service, Cancel);

        DataBroadcastRecord record = taking.Record!;
        Assert.Null(taking.Fault);
        Assert.Equal(2, record.Modules);
        Assert.Equal(
            Payloads(live, DataBroadcastFrames.ModuleKind).Order(Bytes.Comparer),
            record.Carousels.SelectMany(carousel => carousel.Versions).Select(DataBroadcastFrames.ModulePayload).Order(Bytes.Comparer));
        Assert.Equal(
            Payloads(live, DataBroadcastFrames.EventKind),
            record.Events.Select(message => DataBroadcastFrames.Event(message, 0).Payload.ToArray()));
        Assert.NotEmpty(record.Events);
    }

    [Fact(DisplayName = "BR-BD-005: the record of a broadcast ffmpeg wrote begins where its captions begin, and kept and read back it is the same record")]
    public async Task TheRecordBeginsWhereItsCaptionsBeginAndIsKeptWhole()
    {
        string recorded = await CarryingACarouselAsync(TimeSpan.FromHours(13));
        TimeSpan begins = FfprobeFileStart.Of(await FfprobeFileStart.AskAsync(Machine, recorded, TimeProvider.System, Cancel))!.Value;

        DataBroadcastRecord record = (await Taker().TakeAsync(recorded, Service, Cancel)).Record!;
        DataBroadcastShelf shelf = new(new CaptionSettings { WrittenTo = Path.Combine(room, "captions") });
        RecordingId id = RecordingId.New();
        await shelf.KeepAsync(id, record, Cancel);

        Assert.InRange((record.Start - begins).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.All(
            record.Carousels.SelectMany(carousel => carousel.Versions),
            version => Assert.InRange(version.FirstSeenAt - record.Start, TimeSpan.FromSeconds(-2), TimeSpan.FromSeconds(6)));
        Assert.Equal(DataBroadcastRecordFormat.Written(record), DataBroadcastRecordFormat.Written((await shelf.ReadAsync(id, Cancel))!));
    }

    private static TransportStreamDataBroadcastTaker Taker()
        => new(new FfprobeRecordingClockStart(Machine, TimeProvider.System), new CaptionSettings(), TimeProvider.System);

    private static async Task<IReadOnlyList<LiveFrame>> LiveAsync(byte[] broadcast)
    {
        DataBroadcastSession session = new(Service, NullLogger.Instance);
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using ILiveViewing viewing = (await fanout.JoinAsync(Cancel))!;
        session.Show(fanout);

        await session.Seat.WriteAsync(broadcast, Cancel);

        List<LiveFrame> frames = [];

        while (viewing.Frames.TryRead(out LiveFrame? frame))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static IEnumerable<byte[]> Payloads(IReadOnlyList<LiveFrame> frames, byte kind)
        => frames.Where(frame => frame.Payload.Span[0] == kind).Select(frame => frame.Payload.ToArray());

    private async Task<string> CarryingACarouselAsync(TimeSpan startsAt)
    {
        string recorded = Path.Combine(room, "recording.m2ts");

        await File.WriteAllBytesAsync(recorded, await SplicedCarousel.CarryingACarouselAsync(room, startsAt), Cancel);

        return recorded;
    }

    private sealed class Bytes : IComparer<byte[]>
    {
        public static readonly Bytes Comparer = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}
