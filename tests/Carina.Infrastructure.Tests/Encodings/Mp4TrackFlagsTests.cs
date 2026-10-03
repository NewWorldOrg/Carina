using System.Buffers.Binary;
using System.Text;

using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class Mp4TrackFlagsTests : IDisposable
{
    private const int Enabled = 0x000001;

    private const int InMovie = 0x000002;

    private readonly string room = Directory.CreateTempSubdirectory("carina-mp4-flags-").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public void BrEd2019TheSubtitleTrackIsNoLongerEnabledAndNoOtherTrackIsTouched()
    {
        string file = Written(
            Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0")),
            Box("moov", [.. Track("vide", Enabled | InMovie), .. Track("soun", Enabled | InMovie), .. Track("sbtl", Enabled | InMovie), .. Track("text", 0)]),
            Box("mdat", new byte[64]));

        Assert.Equal(1, Mp4TrackFlags.TurnOffSubtitles(file));

        Assert.Equal([Enabled | InMovie, Enabled | InMovie, InMovie, 0], FlagsOf(file));
    }

    [Fact]
    public void BrEd2019TheTracksAreFoundWhereverTheMovieBoxStandsAndWhateverSizeTheBoxesBefore()
    {
        string file = Written(
            Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0")),
            LargeBox("mdat", new byte[100]),
            Box("free", new byte[3]),
            Box("moov", [.. Track("vide", Enabled), .. Track("sbtl", Enabled)]));

        Assert.Equal(1, Mp4TrackFlags.TurnOffSubtitles(file));

        Assert.Equal([Enabled, 0], FlagsOf(file));
    }

    [Fact]
    public void AFileWithNoSubtitleTrackOrNoMovieIsLeftAsItIs()
    {
        string noSubtitles = Written(Box("moov", [.. Track("vide", Enabled)]));
        string noMovie = Written(Box("mdat", new byte[16]));
        string cutShort = Written(Box("moov", [.. Track("sbtl", Enabled)])[..20]);
        byte[] before = File.ReadAllBytes(noSubtitles);

        Assert.Equal(0, Mp4TrackFlags.TurnOffSubtitles(noSubtitles));
        Assert.Equal(0, Mp4TrackFlags.TurnOffSubtitles(noMovie));
        Assert.Equal(0, Mp4TrackFlags.TurnOffSubtitles(cutShort));
        Assert.Equal(before, File.ReadAllBytes(noSubtitles));
    }

    private string Written(params byte[][] boxes)
    {
        string file = Path.Combine(room, Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(file, [.. boxes.SelectMany(box => box)]);

        return file;
    }

    private static IReadOnlyList<int> FlagsOf(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        List<int> flags = [];

        for (int at = 0; at + 8 <= bytes.Length; at++)
        {
            if (bytes.AsSpan(at, 4).SequenceEqual("tkhd"u8))
            {
                flags.Add((bytes[at + 5] << 16) | (bytes[at + 6] << 8) | bytes[at + 7]);
            }
        }

        return flags;
    }

    private static byte[] Track(string handler, int flags)
    {
        byte[] header = new byte[84];
        header[1] = (byte)(flags >> 16);
        header[2] = (byte)(flags >> 8);
        header[3] = (byte)flags;

        byte[] reference = new byte[24];
        Encoding.ASCII.GetBytes(handler).CopyTo(reference, 8);

        return Box("trak", [.. Box("tkhd", header), .. Box("mdia", [.. Box("mdhd", new byte[24]), .. Box("hdlr", reference)])]);
    }

    private static byte[] Box(string type, byte[] body)
    {
        byte[] box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }

    private static byte[] LargeBox(string type, byte[] body)
    {
        byte[] box = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        body.CopyTo(box, 16);

        return box;
    }
}
