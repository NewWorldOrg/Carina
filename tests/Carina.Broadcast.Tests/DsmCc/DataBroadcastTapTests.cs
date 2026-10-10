using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Sections;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class DataBroadcastTapTests
{
    private const long Second = CarouselBroadcast.Second;

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml><body></body></bml>")));

    [Fact(DisplayName = "BR-BD-004: nothing is read of the data broadcast until the programme's clock is heard, and then everything is read at it")]
    public void NothingIsReadUntilTheClockIsHeard()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);

        Assert.Empty(tap.Push(new CarouselBroadcast().Associated().Mapped().Listed(1, Startup).Delivered(Startup).Bytes));

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(new CarouselBroadcast().At(Second).Listed(1, Startup).Delivered(Startup).Bytes);

        Assert.Equal(
            [typeof(DataBroadcastRead.ServiceMapped), typeof(DataBroadcastRead.CarouselChanged), typeof(DataBroadcastRead.CarouselChanged)],
            reads.Select(read => read.GetType()));
        Assert.All(reads, read => Assert.Equal(Second, read.At));
        DataBroadcastService service = Assert.IsType<DataBroadcastRead.ServiceMapped>(reads[0]).Service;
        Assert.Equal(CarouselBroadcast.EntryTag, service.Entry?.ComponentTag);
        Assert.IsType<CarouselChange.CatalogueUpdated>(Assert.IsType<DataBroadcastRead.CarouselChanged>(reads[1]).Change);
        CompletedModule module = Assert.IsType<CarouselChange.ModuleCompleted>(Assert.IsType<DataBroadcastRead.CarouselChanged>(reads[2]).Change).Module;
        Assert.Equal("startup.bml", Assert.Single(module.Resources).Location);
    }

    [Fact(DisplayName = "BR-BD-001: a programme whose map carries no data stream is read as carrying no data broadcast")]
    public void AProgrammeWithoutADataStreamIsReadAsCarryingNone()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(new CarouselBroadcast().Associated().Mapped(carrying: false).At(Second).Bytes);

        Assert.False(Assert.IsType<DataBroadcastRead.ServiceMapped>(Assert.Single(reads)).Service.IsCarried);
    }

    [Fact]
    public void TheSameMapReadAgainSaysNothingAndPutsNothingTogetherAgain()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Delivered(Startup).Bytes);

        Assert.Empty(tap.Push(new CarouselBroadcast().Associated().Mapped().At(2 * Second).Listed(1, Startup).Delivered(Startup).Bytes));
    }

    [Fact(DisplayName = "BR-BD-001: a new version of the programme map starts the carousels again")]
    public void ANewVersionOfTheMapStartsTheCarouselsAgain()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Delivered(Startup).Bytes);

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(new CarouselBroadcast()
            .Mapped(version: 1)
            .At(2 * Second)
            .Listed(1, Startup)
            .Delivered(Startup)
            .Bytes);

        Assert.IsType<DataBroadcastRead.ServiceMapped>(reads[0]);
        Assert.Contains(reads, read => read is DataBroadcastRead.CarouselChanged { Change: CarouselChange.ModuleCompleted });
    }

    [Theory(DisplayName = "BR-BD-004: a stream handed over in pieces of any length is read as it is read whole")]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(187)]
    [InlineData(189)]
    [InlineData(1000)]
    public void AStreamHandedOverInPiecesIsReadAsItIsReadWhole(int piece)
    {
        byte[] stream = new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Delivered(Startup).At(2 * Second).Fired(0, 1, 2, 3).Bytes;
        DataBroadcastTap whole = new(CarouselBroadcast.ProgramNumber);
        DataBroadcastTap pieces = new(CarouselBroadcast.ProgramNumber);

        IReadOnlyList<string> expected = Described(whole.Push(stream));
        List<DataBroadcastRead> read = [];

        for (int at = 0; at < stream.Length; at += piece)
        {
            read.AddRange(pieces.Push(stream.AsSpan(at, Math.Min(piece, stream.Length - at))));
        }

        Assert.Equal(4, expected.Count);
        Assert.Equal(expected, Described(read));
        Assert.Equal(0, pieces.UnreadablePackets);
    }

    [Fact]
    public void BytesThatAreNoPacketAreSkippedAsFarAsTheNextSyncByte()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        byte[] stream = [0x00, 0x01, 0x02, .. new CarouselBroadcast().Associated().Mapped().At(Second).Bytes];

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(stream);

        Assert.IsType<DataBroadcastRead.ServiceMapped>(Assert.Single(reads));
        Assert.Equal(1, tap.UnreadablePackets);
    }

    [Fact(DisplayName = "BR-BD-003: an event message fired at once is timed at the programme's clock, followed through its wrap")]
    public void AnEventFiredAtOnceIsTimedAtTheClockFollowedThroughItsWrap()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(ProgramClock.Modulus - Second).Bytes);

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(new CarouselBroadcast().At(Second).Fired(0, 5, 6, 7, 0xAB).Bytes);

        DataBroadcastRead.EventMessageTimed timed = Assert.IsType<DataBroadcastRead.EventMessageTimed>(Assert.Single(reads));
        Assert.Equal(ProgramClock.Modulus + Second, timed.At);
        Assert.Equal(ProgramClock.Modulus + Second, timed.FiresAt);
        Assert.Equal((5, 6, 7), (timed.Message.EventMessageGroupId, timed.Message.EventMessageId, timed.Message.EventMessageType));
        Assert.Equal([0xAB], timed.Message.PrivateData.ToArray());
        Assert.Equal(ProgramClock.Modulus + Second, tap.Now);
    }

    [Fact]
    public void TheMapOfAnotherProgrammeIsNotRead()
    {
        DataBroadcastTap mappedElsewhere = new(CarouselBroadcast.ProgramNumber);
        DataBroadcastTap associatedElsewhere = new(CarouselBroadcast.ProgramNumber);

        Assert.Empty(mappedElsewhere.Push(new CarouselBroadcast().Associated().Mapped(programNumber: CarouselBroadcast.ProgramNumber + 1).At(Second).Bytes));
        Assert.Empty(associatedElsewhere.Push(new CarouselBroadcast().Associated(CarouselBroadcast.ProgramNumber + 1).Mapped().At(Second).Bytes));
    }

    [Fact]
    public void OnlyTheClockTheMapNamesIsFollowed()
    {
        const int ElsewherePid = 0x0111;
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);

        Assert.Empty(tap.Push(new CarouselBroadcast().Associated().Mapped(clockPid: ElsewherePid).At(Second).Bytes));
        Assert.Null(tap.Now);

        Assert.Equal(2 * Second, Assert.Single(tap.Push(new CarouselBroadcast().At(2 * Second, ElsewherePid).Bytes)).At);
    }

    [Fact(DisplayName = "BR-BD-004: a programme clock carried in a packet the demodulator marked as broken is not followed")]
    public void AClockInAPacketMarkedBrokenIsNotFollowed()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);

        Assert.Empty(tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second, transportError: true).Bytes));
        Assert.Null(tap.Now);

        tap.Push(new CarouselBroadcast().At(2 * Second).At(5 * Second, transportError: true).Bytes);

        Assert.Equal(2 * Second, tap.Now);
    }

    [Fact(DisplayName = "BR-BD-005: the first moment of the programme's clock heard is kept while the clock goes on and comes around")]
    public void TheFirstMomentOfTheClockHeardIsKept()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second, transportError: true).Bytes);

        Assert.Null(tap.FirstHeard);

        tap.Push(new CarouselBroadcast().At(ProgramClock.Modulus - Second).At(Second).At(3 * Second).Bytes);

        Assert.Equal(ProgramClock.Modulus - Second, tap.FirstHeard);
        Assert.Equal(ProgramClock.Modulus + (3 * Second), tap.Now);
    }

    [Fact(DisplayName = "BR-BD-004: a programme clock that steps back does not move what is read back with it")]
    public void AClockThatStepsBackDoesNotMoveWhatIsReadBack()
    {
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second).Bytes);

        IReadOnlyList<DataBroadcastRead> reads = tap.Push(new CarouselBroadcast()
            .At(ProgramClock.Modulus - Second)
            .Listed(1, Startup)
            .Delivered(Startup)
            .Bytes);

        Assert.Equal(Second, tap.Now);
        Assert.All(reads, read => Assert.Equal(Second, read.At));

        tap.Push(new CarouselBroadcast().At(3 * Second).Bytes);

        Assert.Equal(3 * Second, tap.Now);
    }

    [Fact(DisplayName = "BR-BV-001: no stream of random packets on the pids it reads makes the tap throw")]
    public void NoStreamOfRandomPacketsMakesTheTapThrow()
    {
        Random random = new(20261010);
        DataBroadcastTap tap = new(CarouselBroadcast.ProgramNumber);
        int[] pids = [0x0000, CarouselBroadcast.MapPid, CarouselBroadcast.ClockPid, CarouselBroadcast.CarouselPid];
        tap.Push(new CarouselBroadcast().Associated().Mapped().At(Second).Listed(1, Startup).Bytes);

        for (int round = 0; round < 4000; round++)
        {
            byte[] packet = new byte[TransportPacket.Size];
            random.NextBytes(packet);
            packet[0] = TransportPacket.SyncByte;
            int pid = pids[round % pids.Length];
            packet[1] = (byte)((packet[1] & 0xE0) | (pid >> 8));
            packet[2] = (byte)pid;

            _ = tap.Push(round % 7 == 0 ? packet.AsSpan(0, random.Next(0, TransportPacket.Size)) : packet);
        }

        _ = tap.Push(new CarouselBroadcast().Associated().Mapped(version: 3).At(9 * Second).Listed(2, Startup).Delivered(Startup).Bytes);
    }

    private static IReadOnlyList<string> Described(IEnumerable<DataBroadcastRead> reads)
        => [.. reads.Select(read => read switch
        {
            DataBroadcastRead.CarouselChanged changed => $"{read.At} {changed.Change.GetType().Name}",
            DataBroadcastRead.EventMessageTimed timed => $"{read.At} event {timed.FiresAt}",
            _ => $"{read.At} {read.GetType().Name}",
        })];
}
