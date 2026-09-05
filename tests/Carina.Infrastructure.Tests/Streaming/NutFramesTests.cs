using System.Buffers.Binary;
using System.Text;

using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class NutFramesTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 1, 2, 3];

    [Fact]
    public void AFrameComesOutWithTheStampTheContainerCarriesForItIn90kHz()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(90_000, [(6_908_706_000L, Png)]));

        Assert.Null(read.Fault);
        NutFrame frame = Assert.Single(read.Frames);
        Assert.Equal(6_908_706_000UL, frame.Pts.Value);
        Assert.Equal(Png, frame.Data.ToArray());
    }

    [Fact]
    public void AStampInAnotherClockIsRescaledTo90kHz()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(1_000, [(76_764_152L, Png)]));

        Assert.Equal(76_764_152UL * 90, Assert.Single(read.Frames).Pts.Value);
    }

    [Fact]
    public void FramesArriveInWhateverPiecesThePipeHandsOverAndComeOutWhole()
    {
        byte[] whole = Written.Of(90_000, [(100L, Png), (200L, [.. Enumerable.Range(0, 5_000).Select(at => (byte)at)]), (300L, Png)]);
        NutFrames frames = new();
        List<NutFrame> gathered = [];

        foreach (byte one in whole)
        {
            NutReading read = frames.Read([one]);

            Assert.Null(read.Fault);
            gathered.AddRange(read.Frames);
        }

        Assert.Equal([100UL, 200UL, 300UL], gathered.Select(frame => frame.Pts.Value));
        Assert.Equal(5_000, gathered[1].Data.Length);
        Assert.Null(frames.Ended().Fault);
    }

    [Fact]
    public void AStampCodedShortIsReadAgainstTheOneBeforeIt()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(90_000, [(1_000_000L, Png), (1_000_100L, Png), (999_950L, Png)], codeShort: true));

        Assert.Null(read.Fault);
        Assert.Equal([1_000_000UL, 1_000_100UL, 999_950UL], read.Frames.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public void ASyncpointResetsTheClockEveryFrameAfterItIsReadAgainst()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(90_000, [(500L, Png), (700L, Png)], codeShort: true, syncpointBeforeEach: true));

        Assert.Equal([500UL, 700UL], read.Frames.Select(frame => frame.Pts.Value));
    }

    [Fact]
    public void AnElidedHeaderIsPutBackInFrontOfTheFrame()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(90_000, [(100L, [0x00, 0x00, 0x01, 0xB6, 9, 9])], elide: true));

        Assert.Equal([0x00, 0x00, 0x01, 0xB6, 9, 9], Assert.Single(read.Frames).Data.ToArray());
    }

    [Fact]
    public void PacketsTheReaderDoesNotCareAboutAreSteppedOver()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Written.Of(90_000, [(100L, Png)], infoPacket: true));

        Assert.Null(read.Fault);
        Assert.Single(read.Frames);
    }

    [Fact]
    public void SomethingThatIsNotNutIsAFaultAtOnce()
    {
        NutFrames frames = new();

        NutReading read = frames.Read(Encoding.ASCII.GetBytes("ftyp....................."));

        Assert.Equal(NutFault.NotTheContainerItWasAskedFor, read.Fault);
        Assert.Equal(NutFault.NotTheContainerItWasAskedFor, frames.Read([1]).Fault);
    }

    [Fact]
    public void AFrameCodeTheHeaderNeverDefinedIsAFault()
    {
        byte[] whole = Written.Of(90_000, [(100L, Png)]);
        int frameAt = whole.Length - Png.Length - Written.FrameHeaderLength(100L, Png.Length);

        whole[frameAt] = (byte)'N' + 1;

        NutFrames frames = new();

        Assert.Equal(NutFault.AFrameCodeNobodyDefined, frames.Read(whole).Fault);
    }

    [Fact]
    public void AFrameNoCaptionCouldBeThatBigIsAFaultNotAnAllocation()
    {
        byte[] whole = Written.Of(90_000, [(100L, Png)], claimedSize: NutFrames.LargestFrame + 1);
        NutFrames frames = new();

        Assert.Equal(NutFault.AFrameTooBigToHold, frames.Read(whole).Fault);
    }

    [Fact]
    public void BytesLeftOverWhenTheStreamEndsAreAFrameThatStoppedPartWay()
    {
        byte[] whole = Written.Of(90_000, [(100L, Png)]);
        NutFrames frames = new();

        Assert.Empty(frames.Read(whole[..^2]).Frames);
        Assert.Equal(NutFault.StoppedPartWayThroughAFrame, frames.Ended().Fault);
    }

    [Fact]
    public void AStreamEndingBetweenFramesEndsWithoutAFault()
    {
        NutFrames frames = new();

        frames.Read(Written.Of(90_000, [(100L, Png)]));

        Assert.Null(frames.Ended().Fault);
    }

    public static class Written
    {
        private const int Shift = 14;

        public static byte[] Of(
            long clockHertz,
            IReadOnlyList<(long Pts, byte[] Data)> frames,
            bool codeShort = false,
            bool syncpointBeforeEach = false,
            bool elide = false,
            bool infoPacket = false,
            long? claimedSize = null)
        {
            using MemoryStream nut = new();

            nut.Write("nut/multimedia container\0"u8);
            Packet(nut, 0x7A561F5F04ADUL + ((((ulong)'N' << 8) + 'M') << 48), MainHeader(clockHertz, elide));
            Packet(nut, 0x11405BF2F9DBUL + ((((ulong)'N' << 8) + 'S') << 48), StreamHeader());

            if (infoPacket)
            {
                Packet(nut, 0xAB68B596BA78UL + ((((ulong)'N' << 8) + 'I') << 48), [1, 2, 3, 4, 5]);
            }

            long last = 0;

            for (int at = 0; at < frames.Count; at++)
            {
                (long pts, byte[] data) = frames[at];

                if (at is 0 || syncpointBeforeEach)
                {
                    Packet(nut, 0xE4ADEECA4569UL + ((((ulong)'N' << 8) + 'K') << 48), Syncpoint(pts));
                    last = pts;
                }

                nut.WriteByte(0);
                Unsigned(nut, codeShort ? Coded(pts, last) : pts + (1L << Shift));
                Unsigned(nut, claimedSize ?? data.Length);
                nut.Write(elide ? data.AsSpan(4) : data);
                last = pts;
            }

            return nut.ToArray();
        }

        public static int FrameHeaderLength(long pts, int size)
            => 1 + Length(pts + (1L << Shift)) + Length(size);

        private static long Coded(long pts, long last)
        {
            long mask = (1L << Shift) - 1;

            return Math.Abs(pts - last) < mask / 2 ? pts & mask : pts + (1L << Shift);
        }

        private static byte[] MainHeader(long clockHertz, bool elide)
        {
            using MemoryStream body = new();

            Unsigned(body, 3);
            Unsigned(body, 1);
            Unsigned(body, 32_767);
            Unsigned(body, 1);
            Unsigned(body, 1);
            Unsigned(body, clockHertz);
            Unsigned(body, 8 | 32);
            Unsigned(body, 8);
            Signed(body, 0);
            Unsigned(body, 1);
            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 1);
            Signed(body, 0);
            Unsigned(body, elide ? 1 : 0);
            Unsigned(body, 8192);
            Unsigned(body, 6);
            Signed(body, 0);
            Unsigned(body, 1);
            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 254);
            Unsigned(body, 1);
            Unsigned(body, 4);
            body.Write([0x00, 0x00, 0x01, 0xB6]);

            return body.ToArray();
        }

        private static byte[] StreamHeader()
        {
            using MemoryStream body = new();

            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 4);
            body.Write("PNG "u8);
            Unsigned(body, 0);
            Unsigned(body, Shift);
            Unsigned(body, 90_000);
            Unsigned(body, 0);
            Unsigned(body, 1);
            Unsigned(body, 0);
            Unsigned(body, 1440);
            Unsigned(body, 1080);
            Unsigned(body, 0);
            Unsigned(body, 0);
            Unsigned(body, 0);

            return body.ToArray();
        }

        private static byte[] Syncpoint(long pts)
        {
            using MemoryStream body = new();

            Unsigned(body, pts);
            Unsigned(body, 0);

            return body.ToArray();
        }

        private static void Packet(Stream nut, ulong startcode, byte[] body)
        {
            Span<byte> code = stackalloc byte[8];

            BinaryPrimitives.WriteUInt64BigEndian(code, startcode);
            nut.Write(code);
            Unsigned(nut, body.Length + 4);
            nut.Write(body);
            nut.Write(new byte[4]);
        }

        private static int Length(long value)
        {
            int length = 1;

            while ((value >>= 7) > 0)
            {
                length++;
            }

            return length;
        }

        private static void Unsigned(Stream into, long value)
        {
            int length = Length(value);

            for (int at = length - 1; at >= 0; at--)
            {
                byte piece = (byte)((value >> (7 * at)) & 0x7F);

                into.WriteByte(at is 0 ? piece : (byte)(piece | 0x80));
            }
        }

        private static void Signed(Stream into, long value)
        {
            long temp = value > 0 ? (value * 2) - 1 : -value * 2;

            Unsigned(into, temp);
        }
    }
}
