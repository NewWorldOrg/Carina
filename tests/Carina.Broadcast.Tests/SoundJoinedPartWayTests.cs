using Carina.Broadcast.Sections;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests;

public sealed class SoundJoinedPartWayTests
{
    private const int MapPid = 0x1FC8;

    private const int SoundPid = 0x101;

    private const int OtherPid = 0x100;

    private const int FrameLength = 307;

    private const int FramesPerPes = 2;

    private const int PesWritten = 40;

    private const long FirstPts = 90_000L * 60 * 60 * 13;

    [Fact]
    public void EverySoundPesButTheLastCarriesTheSameNumberOfBytesWhereverTheFramesFall()
    {
        IReadOnlyList<byte[]> carried = SoundJoinedPartWay.Pes(SoundJoinedPartWay.Into(Written(), MapPid), SoundPid);

        Assert.True(carried.Count > 2, $"the sound was carried in {carried.Count} PES");
        Assert.All(carried.SkipLast(1), pes => Assert.Equal(SoundJoinedPartWay.PesBytes, Body(pes).Length));
    }

    [Fact]
    public void TheSoundBeginsPartWayThroughAFrameWhoseLeftoverReadsAsTheHeaderOfALongerFrame()
    {
        byte[] sound = [.. SoundJoinedPartWay.Pes(SoundJoinedPartWay.Into(Written(), MapPid), SoundPid).SelectMany(Body)];
        int join = (SoundJoinedPartWay.FramesBeforeTheJoin * FrameLength) + SoundJoinedPartWay.BytesIntoTheFrame;
        byte[] original = [.. Frames()];
        byte[] header = sound[SoundJoinedPartWay.HeaderIntoTheLeftover..(SoundJoinedPartWay.HeaderIntoTheLeftover + DualMonoAdts.HeaderLength)];

        Assert.Equal(original[join..(join + SoundJoinedPartWay.HeaderIntoTheLeftover)], sound[..SoundJoinedPartWay.HeaderIntoTheLeftover]);
        Assert.Equal(DualMonoAdts.Header(SoundJoinedPartWay.LengthTheHeaderClaims, DualMonoAdts.Stereo), header);
        Assert.Equal(original[(join + SoundJoinedPartWay.HeaderIntoTheLeftover + DualMonoAdts.HeaderLength)..], sound[(SoundJoinedPartWay.HeaderIntoTheLeftover + DualMonoAdts.HeaderLength)..]);
    }

    [Fact]
    public void EachSoundPesIsStampedWithTheTimeOfTheFirstFrameBeginningInsideItAndOneWithNoneIsNotStamped()
    {
        IReadOnlyList<byte[]> carried = SoundJoinedPartWay.Pes(SoundJoinedPartWay.Into(Written(), MapPid), SoundPid);
        int join = (SoundJoinedPartWay.FramesBeforeTheJoin * FrameLength) + SoundJoinedPartWay.BytesIntoTheFrame;
        int from = join;

        foreach (byte[] pes in carried)
        {
            int until = from + Body(pes).Length;
            int? first = Enumerable.Range(0, PesWritten * FramesPerPes)
                .Select(frame => (int?)frame)
                .FirstOrDefault(frame => frame * FrameLength >= from && frame * FrameLength < until);

            Assert.Equal(first is { } begins ? (long?)(FirstPts + (begins * SoundJoinedPartWay.TicksPerFrame)) : null, Stamp(pes));
            from = until;
        }
    }

    [Fact]
    public void EveryOtherPacketIsLeftAsItWasAndTheSoundCounterRunsUnbroken()
    {
        byte[] written = Written();
        byte[] joined = SoundJoinedPartWay.Into(written, MapPid);

        Assert.Equal(Packets(written, pid => pid != SoundPid), Packets(joined, pid => pid != SoundPid));

        int? previous = null;

        foreach (byte[] packet in Packets(joined, pid => pid == SoundPid))
        {
            Assert.True(TransportPacket.TryRead(packet, out TransportPacket read));
            Assert.Equal(previous is { } counter ? (counter + 1) & 0x0F : read.ContinuityCounter, read.ContinuityCounter);
            previous = read.ContinuityCounter;
        }
    }

    private static byte[] Written()
    {
        List<byte> stream =
        [
            .. new TransportStreamWriter(MapPid)
                .Sections(new PmtWriter
                {
                    ProgramNumber = SyntheticBroadcast.SomeProgramNumber,
                    Streams = [PmtWriter.Stream(SoundJoinedPartWay.AdtsSound, SoundPid, [])],
                }.ToBytes())
                .Bytes,
        ];
        TransportStreamWriter sound = new(SoundPid);
        TransportStreamWriter other = new(OtherPid);
        byte[] frames = [.. Frames()];

        for (int pes = 0; pes < PesWritten; pes++)
        {
            int before = sound.Packets.Count;

            stream.AddRange(other.AdaptationOnlyPacket().Packets[^1]);
            PesWriter.Packets(
                sound,
                PesWriter.Sound(FirstPts + (pes * FramesPerPes * SoundJoinedPartWay.TicksPerFrame), frames.AsSpan(pes * FramesPerPes * FrameLength, FramesPerPes * FrameLength)));
            stream.AddRange(sound.Packets.Skip(before).SelectMany(packet => packet));
        }

        return [.. stream];
    }

    private static IEnumerable<byte> Frames()
        => Enumerable.Range(0, PesWritten * FramesPerPes)
            .SelectMany(frame => DualMonoAdts.Header(FrameLength, DualMonoAdts.Stereo)
                .Concat(Enumerable.Range(0, FrameLength - DualMonoAdts.HeaderLength).Select(at => (byte)((frame + at) % 200))));

    private static byte[] Body(byte[] pes) => pes[(PesWriter.HeaderLength + pes[PesWriter.HeaderLength - 1])..];

    private static long? Stamp(byte[] pes) => (pes[7] & 0x80) is 0 ? null : PesWriter.TimestampOf(pes);

    private static List<byte[]> Packets(byte[] stream, Func<int, bool> kept)
        =>
        [
            .. stream.Chunk(TransportStreamWriter.PacketSize)
                .Where(packet => TransportPacket.TryRead(packet, out TransportPacket read) && kept(read.Pid)),
        ];
}
