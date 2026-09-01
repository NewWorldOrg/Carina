using System.Buffers.Binary;
using System.Text;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveFramesTests
{
    [Theory]
    [InlineData(LiveTrack.Picture, LiveFragmentKind.Initialisation, LiveChannel.PictureHeader)]
    [InlineData(LiveTrack.Picture, LiveFragmentKind.Media, LiveChannel.Picture)]
    [InlineData(LiveTrack.Sound, LiveFragmentKind.Initialisation, LiveChannel.SoundHeader)]
    [InlineData(LiveTrack.Sound, LiveFragmentKind.Media, LiveChannel.Sound)]
    public void EachTrackAndKindRidesTheChannelTheWireSetAsideForIt(
        LiveTrack track,
        LiveFragmentKind kind,
        LiveChannel channel)
    {
        Assert.Equal(channel, LiveFrames.Channel(track, kind));
    }

    [Fact]
    public void AHeaderIsHandedOverAtTheStartOfTheClock()
    {
        LiveFrames frames = new();

        LiveFrame frame = frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));

        Assert.Equal(LiveChannel.PictureHeader, frame.Channel);
        Assert.Equal(LivePts.Start, frame.Pts);
    }

    [Fact]
    public void APictureIsStampedWithTheDecodeTimeTheFragmentCarries()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));

        Assert.Equal(LivePts.Of(180_000UL), frames.Of(Fragment(LiveFragmentKind.Media, Media(180_000UL))).Pts);
    }

    [Fact]
    public void ADecodeTimeOnAClockOfAnotherRateIsReadAtNinetyKilohertz()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(15_360U)));

        Assert.Equal(LivePts.Of(90_000UL), frames.Of(Fragment(LiveFragmentKind.Media, Media(15_360UL))).Pts);
    }

    [Fact]
    public void ADecodeTimeOfSixtyFourBitsIsReadWhole()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));

        Assert.Equal(
            LivePts.Of(LivePts.ComesAroundAt + 90_000UL),
            frames.Of(Fragment(LiveFragmentKind.Media, Media(LivePts.ComesAroundAt + 90_000UL, wide: true))).Pts);
    }

    [Fact]
    public void TheEarliestOfTheTracksInAFragmentIsTheOneTheFrameIsStampedWith()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(
            LiveFragmentKind.Initialisation,
            Joined(
                Box("ftyp", new byte[8]),
                Box("moov", Joined(Track(1U, 90_000U), Track(2U, 48_000U))))));

        LiveFrame frame = frames.Of(Fragment(
            LiveFragmentKind.Media,
            Joined(
                Box("moof", Joined(Traf(2U, 48_000UL), Traf(1U, 180_000UL))),
                Box("mdat", new byte[16]))));

        Assert.Equal(LivePts.Of(90_000UL), frame.Pts);
    }

    [Fact]
    public void AFragmentWhoseTrackWasNeverIntroducedKeepsWhereTheClockHadReached()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));
        frames.Of(Fragment(LiveFragmentKind.Media, Media(180_000UL)));

        LiveFrame frame = frames.Of(Fragment(
            LiveFragmentKind.Media,
            Joined(Box("moof", Joined(Traf(7U, 900_000UL))), Box("mdat", new byte[16]))));

        Assert.Equal(LivePts.Of(180_000UL), frame.Pts);
        Assert.Equal(1, frames.Unstamped);
    }

    [Fact]
    public void AFragmentCarryingNoDecodeTimeAtAllKeepsWhereTheClockHadReached()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));

        LiveFrame frame = frames.Of(Fragment(
            LiveFragmentKind.Media,
            Joined(Box("moof", Box("mfhd", new byte[8])), Box("mdat", new byte[16]))));

        Assert.Equal(LivePts.Start, frame.Pts);
        Assert.Equal(1, frames.Unstamped);
    }

    [Fact]
    public void NothingIsCountedAsUnstampedWhileEveryFragmentSaysWhenItIs()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Head(90_000U)));
        frames.Of(Fragment(LiveFragmentKind.Media, Media(180_000UL)));
        frames.Of(Fragment(LiveFragmentKind.Media, Media(198_000UL)));

        Assert.Equal(0, frames.Unstamped);
    }

    [Fact]
    public void ThePayloadHandedOverIsTheFragmentItself()
    {
        LiveFrames frames = new();
        byte[] head = Head(90_000U);

        Assert.Equal(head, frames.Of(Fragment(LiveFragmentKind.Initialisation, head)).Payload.ToArray());
    }

    [Fact]
    public void AHeaderWithAClockOfNoRateIsReadAsCarryingNoTrackAtAll()
    {
        LiveFrames frames = new();

        frames.Of(Fragment(LiveFragmentKind.Initialisation, Joined(
            Box("ftyp", new byte[8]),
            Box("moov", Track(1U, 0U)))));

        Assert.Equal(LivePts.Start, frames.Of(Fragment(LiveFragmentKind.Media, Media(180_000UL))).Pts);
        Assert.Equal(1, frames.Unstamped);
    }

    [Fact]
    public void ABoxSayingItIsLongerThanWhatIsThereStopsTheWalkRatherThanReadingPastIt()
    {
        LiveFrames frames = new();
        byte[] head = Head(90_000U);

        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(16), 0xffff_ffffU);

        frames.Of(Fragment(LiveFragmentKind.Initialisation, head));

        Assert.Equal(LivePts.Start, frames.Of(Fragment(LiveFragmentKind.Media, Media(180_000UL))).Pts);
    }

    private static LiveFragment Fragment(LiveFragmentKind kind, byte[] bytes)
        => new(LiveTrack.Picture, kind, bytes);

    private static byte[] Head(uint timescale)
        => Joined(Box("ftyp", new byte[8]), Box("moov", Track(1U, timescale)));

    private static byte[] Media(ulong decodeTime, bool wide = false)
        => Joined(Box("moof", Joined(Box("mfhd", new byte[8]), Traf(1U, decodeTime, wide))), Box("mdat", new byte[16]));

    private static byte[] Track(uint id, uint timescale)
        => Box("trak", Joined(Tkhd(id), Box("mdia", Mdhd(timescale))));

    private static byte[] Tkhd(uint id)
    {
        byte[] body = new byte[20];

        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), id);

        return Box("tkhd", body);
    }

    private static byte[] Mdhd(uint timescale)
    {
        byte[] body = new byte[20];

        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), timescale);

        return Box("mdhd", body);
    }

    private static byte[] Traf(uint id, ulong decodeTime, bool wide = false)
    {
        byte[] tfhd = new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4), id);

        byte[] tfdt = new byte[wide ? 12 : 8];

        tfdt[0] = wide ? (byte)1 : (byte)0;

        if (wide)
        {
            BinaryPrimitives.WriteUInt64BigEndian(tfdt.AsSpan(4), decodeTime);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(tfdt.AsSpan(4), (uint)decodeTime);
        }

        return Box("traf", Joined(Box("tfhd", tfhd), Box("tfdt", tfdt)));
    }

    private static byte[] Box(string name, byte[] body)
    {
        byte[] box = new byte[8 + body.Length];

        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }

    private static byte[] Joined(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
