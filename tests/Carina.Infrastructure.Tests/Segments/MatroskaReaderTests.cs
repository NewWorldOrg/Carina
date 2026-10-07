using Carina.Infrastructure.Segments;

using static Carina.Infrastructure.Tests.Segments.MatroskaBytes;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class MatroskaReaderTests
{
    private const long BroadcastFrame = 33_366_666;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "the tracks and the blocks of a segment and clusters of unknown size are read, everything else passed over")]
    public void TracksAndBlocksOfUnsizedSegmentAndClustersAreRead()
    {
        Heard heard = Read(AsFfmpegWritesIt());

        Assert.Equal(
            [
                new MatroskaTrack(1, MatroskaTrackKind.Video, "V_UNCOMPRESSED", TimeSpan.FromTicks(333_666), 64, 36, null, null, null),
                new MatroskaTrack(2, MatroskaTrackKind.Audio, "A_PCM/INT/LIT", null, null, null, 8000, 2, 16),
            ],
            heard.Tracks);
        Assert.Equal(
            [
                (2, TimeSpan.Zero, 4),
                (2, TimeSpan.FromMilliseconds(21), 4),
                (1, TimeSpan.FromMilliseconds(33), 3),
                (1, TimeSpan.FromMilliseconds(41 + 26), 3),
                (2, TimeSpan.FromMilliseconds(41 - 1), 4),
            ],
            heard.Blocks.Select(block => (block.Track, block.At, block.Payload.Length)));
        Assert.Equal([9, 9, 9], heard.Blocks[2].Payload);
    }

    [Fact(DisplayName = "bytes handed over one at a time are read the same as bytes handed over at once")]
    public void BytesHandedOverOneAtATimeAreReadTheSame()
    {
        byte[] whole = AsFfmpegWritesIt();
        Heard atOnce = Read(whole);
        Heard heard = new();
        MatroskaReader reader = new(heard);
        List<byte> held = [];

        foreach (byte part in whole)
        {
            held.Add(part);
            held.RemoveRange(0, reader.ReadFrom([.. held]));
        }

        Assert.Empty(held);
        Assert.Equal(atOnce.Tracks, heard.Tracks);
        Assert.Equal(Described(atOnce), Described(heard));
    }

    [Fact(DisplayName = "clusters of known size, a block in a group and a timestamp scale of its own are read")]
    public void SizedClustersBlockGroupsAndAScaleOfItsOwnAreRead()
    {
        byte[] stream =
        [
            .. Header(),
            .. Element(
                Segment,
                Element(Info, Number(TimestampScale, 100_000)),
                Element(Tracks, SoundTrack(1)),
                Element(Cluster, Number(ClusterTimestamp, 10), Element(BlockGroup, Blocked(Block, 1, 5, [1, 2]))),
                Element(Cluster, Number(ClusterTimestamp, 20), Blocked(SimpleBlock, 1, -3, [3]))),
        ];

        Heard heard = Read(stream);

        Assert.Equal(
            [(1, TimeSpan.FromMilliseconds(1.5), 2), (1, TimeSpan.FromMilliseconds(1.7), 1)],
            heard.Blocks.Select(block => (block.Track, block.At, block.Payload.Length)));
    }

    [Fact(DisplayName = "a stream cut inside a block hands over the whole blocks before it and says it was cut short")]
    public async Task AStreamCutInsideABlockSaysItWasCutShort()
    {
        byte[] whole = AsFfmpegWritesIt(withCues: false);
        Heard heard = new();
        int afterEach = 0;

        bool ended = await new MatroskaReader(heard).ReadToEndAsync(
            new MemoryStream(whole[..^2]),
            _ =>
            {
                afterEach++;

                return Task.CompletedTask;
            },
            Cancel);

        Assert.False(ended);
        Assert.Equal(4, heard.Blocks.Count);
        Assert.True(afterEach >= 1);
    }

    [Fact(DisplayName = "a stream read to its end between two elements says so, however small the pieces it comes in")]
    public async Task AStreamReadToItsEndSaysSo()
    {
        Heard heard = new();

        bool ended = await new MatroskaReader(heard).ReadToEndAsync(new Trickle(AsFfmpegWritesIt(), 7), _ => Task.CompletedTask, Cancel);

        Assert.True(ended);
        Assert.Equal(5, heard.Blocks.Count);
    }

    [Fact(DisplayName = "a stream cut while an element is being passed over says it was cut short")]
    public async Task AStreamCutWhilePassingOverSaysItWasCutShort()
    {
        byte[] stream = [.. Header(), .. Unsized(Segment), .. Filler(Padding, 40)];

        bool ended = await new MatroskaReader(new Heard()).ReadToEndAsync(new MemoryStream(stream[..^10]), _ => Task.CompletedTask, Cancel);

        Assert.False(ended);
    }

    [Fact(DisplayName = "a block larger than the first buffer is read whole")]
    public async Task ABlockLargerThanTheFirstBufferIsReadWhole()
    {
        byte[] picture = new byte[MatroskaReader.FirstBuffer + 1000];
        picture[^1] = 7;
        byte[] stream =
        [
            .. Header(),
            .. Unsized(Segment),
            .. Element(Tracks, FrameTrack(1, 480, 270, BroadcastFrame)),
            .. Unsized(Cluster),
            .. Number(ClusterTimestamp, 0),
            .. Blocked(SimpleBlock, 1, 0, picture),
        ];
        Heard heard = new();

        bool ended = await new MatroskaReader(heard).ReadToEndAsync(new MemoryStream(stream), _ => Task.CompletedTask, Cancel);

        Assert.True(ended);
        Assert.Equal(picture, Assert.Single(heard.Blocks).Payload);
    }

    [Theory(DisplayName = "bytes that are not Matroska this reader reads are refused")]
    [InlineData("no header")]
    [InlineData("laced")]
    [InlineData("unsized track")]
    [InlineData("block before its cluster's time")]
    [InlineData("track without a number")]
    public void BytesItDoesNotReadAreRefused(string shape)
    {
        byte[] stream = shape switch
        {
            "no header" => [.. Unsized(Segment)],
            "laced" => [.. Header(), .. Unsized(Segment), .. Unsized(Cluster), .. Number(ClusterTimestamp, 0), .. Blocked(SimpleBlock, 1, 0, [1], KeyFrame | 2)],
            "unsized track" => [.. Header(), .. Unsized(Segment), .. Unsized(Tracks)],
            "block before its cluster's time" => [.. Header(), .. Unsized(Segment), .. Unsized(Cluster), .. Blocked(SimpleBlock, 1, 0, [1])],
            _ => [.. Header(), .. Unsized(Segment), .. Element(Tracks, Element(TrackEntry, Number(TrackType, 1))), .. Filler(Padding, 1)],
        };

        Assert.Throws<InvalidDataException>(() => new MatroskaReader(new Heard()).ReadFrom(stream));
    }

    private static byte[] AsFfmpegWritesIt(bool withCues = true)
        =>
        [
            .. Header(),
            .. Unsized(Segment),
            .. Element(SeekHead, Filler(Crc, 4), Filler(Padding, 11)),
            .. Filler(Padding, 20),
            .. Element(Info, Filler(Crc, 4), Number(TimestampScale, 1_000_000)),
            .. Element(Tracks, Filler(Crc, 4), FrameTrack(1, 64, 36, BroadcastFrame), SoundTrack(2)),
            .. Element(Tags, Filler(Crc, 4), Filler(Padding, 30)),
            .. Unsized(Cluster),
            .. Filler(Crc, 4),
            .. Number(ClusterTimestamp, 0),
            .. Blocked(SimpleBlock, 2, 0, [1, 2, 3, 4]),
            .. Blocked(SimpleBlock, 2, 21, [1, 2, 3, 4]),
            .. Blocked(SimpleBlock, 1, 33, [9, 9, 9]),
            .. Unsized(Cluster),
            .. Number(ClusterTimestamp, 41),
            .. Blocked(SimpleBlock, 1, 26, [8, 8, 8]),
            .. Blocked(SimpleBlock, 2, -1, [1, 2, 3, 4]),
            .. withCues ? Filler(Cues, 12) : [],
        ];

    private static Heard Read(byte[] stream)
    {
        Heard heard = new();
        MatroskaReader reader = new(heard);

        Assert.Equal(stream.Length, reader.ReadFrom(stream));

        return heard;
    }

    private static List<(int, TimeSpan, string)> Described(Heard heard)
        => [.. heard.Blocks.Select(block => (block.Track, block.At, Convert.ToHexString(block.Payload)))];

    private sealed class Heard : IMatroskaSink
    {
        public List<MatroskaTrack> Tracks { get; } = [];

        public List<(int Track, TimeSpan At, byte[] Payload)> Blocks { get; } = [];

        void IMatroskaSink.Tracks(IReadOnlyList<MatroskaTrack> tracks) => Tracks.AddRange(tracks);

        public void Block(int track, TimeSpan at, ReadOnlySpan<byte> payload) => Blocks.Add((track, at, payload.ToArray()));
    }

    private sealed class Trickle(byte[] bytes, int most) : Stream
    {
        private int at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => at;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int taken = Math.Min(Math.Min(count, most), bytes.Length - at);
            Array.Copy(bytes, at, buffer, offset, taken);
            at += taken;

            return taken;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
