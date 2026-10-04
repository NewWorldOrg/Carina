using System.Runtime.Versioning;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Playback;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Playback;

[SupportedOSPlatform("linux")]
public sealed class FfprobeArtefactCodecsTests : IDisposable
{
    private static readonly OutputRoot Shelf = new("shelf");

    private static readonly RecordingFileName Named = new("a1b2c3.d4e5f6.mp4");

    private readonly StandIns standIns = new();

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-artefact-codec-");

    public void Dispose()
    {
        standIns.Dispose();
        shelved.Delete(recursive: true);
    }

    [Fact]
    public void TheFirstPictureTrackIsAskedForItsCodecByKey()
    {
        string[] arguments = [.. FfprobeArtefactCodecs.Arguments("/srv/encodes/a.mp4")];

        Assert.Equal("v:0", arguments[arguments.IndexOf("-select_streams") + 1]);
        Assert.Equal("stream=codec_name", arguments[arguments.IndexOf("-show_entries") + 1]);
        Assert.Equal("default=nw=1", arguments[arguments.IndexOf("-of") + 1]);
        Assert.Equal("/srv/encodes/a.mp4", arguments[arguments.IndexOf("-i") + 1]);
    }

    [Theory]
    [InlineData("h264", EncodeCodec.H264)]
    [InlineData("hevc", EncodeCodec.H265)]
    public async Task APictureTrackOfOneOfTheTwoCodecsOnOfferIsReadAsThatCodec(string said, EncodeCodec codec)
    {
        ArtefactCodecReading reading = await Codecs(standIns.Script($"printf 'codec_name={said}\\n'"))
            .ReadAsync(Placed(900), CancellationToken.None);

        Assert.True(reading.Read);
        Assert.Equal(codec, reading.Codec);
    }

    [Fact]
    public async Task APictureTrackOfAnotherCodecIsReadAsNeitherOfTheTwo()
    {
        ArtefactCodecReading reading = await Codecs(standIns.Script("printf 'codec_name=av1\\n'"))
            .ReadAsync(Placed(900), CancellationToken.None);

        Assert.True(reading.Read);
        Assert.Null(reading.Codec);
        Assert.Equal("av1", reading.Note);
    }

    [Fact]
    public async Task AProgrammeThatRefusedLeavesTheFileUnread()
    {
        ArtefactCodecReading reading = await Codecs(standIns.Script("printf 'Invalid data found\\n' >&2; exit 1"))
            .ReadAsync(Placed(900), CancellationToken.None);

        Assert.False(reading.Read);
        Assert.Contains("Invalid data found", reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileWithNoPictureTrackIsUnreadRatherThanNeitherOfTheTwo()
    {
        ArtefactCodecReading reading = await Codecs(standIns.Script("exit 0"))
            .ReadAsync(Placed(900), CancellationToken.None);

        Assert.False(reading.Read);
    }

    [Fact]
    public async Task AProbeThatIsNotOnThisMachineLeavesTheFileUnreadRatherThanThrowing()
    {
        ArtefactCodecReading reading = await Codecs(standIns.Named("no-such-programme"))
            .ReadAsync(Placed(900), CancellationToken.None);

        Assert.False(reading.Read);
    }

    [Fact]
    public async Task AFileThatIsNotOnTheDiskIsUnreadAndTheProbeIsNotStarted()
    {
        string asked = standIns.Named("asked");
        IArtefactCodecReader codecs = Codecs(standIns.Script($"touch '{asked}'; printf 'codec_name=h264\\n'"));

        ArtefactCodecReading reading = await codecs.ReadAsync(
            new PlaybackFile(Shelf, Named, 900),
            CancellationToken.None);

        Assert.False(reading.Read);
        Assert.False(File.Exists(asked));
    }

    [Fact]
    public async Task AFileReadOnceIsNotProbedAgainWhileItStaysAsItWas()
    {
        string asked = standIns.Named("asked");
        IArtefactCodecReader codecs = Codecs(standIns.Script($"echo x >> '{asked}'; printf 'codec_name=h264\\n'"));
        PlaybackFile file = Placed(900);

        await codecs.ReadAsync(file, CancellationToken.None);
        ArtefactCodecReading again = await codecs.ReadAsync(file, CancellationToken.None);

        Assert.Equal(EncodeCodec.H264, again.Codec);
        Assert.Single(File.ReadAllLines(asked));
    }

    [Fact]
    public async Task AFileReplacedByAnotherOfTheSameNameIsReadAgain()
    {
        string said = standIns.Named("said");
        File.WriteAllText(said, "h264");
        IArtefactCodecReader codecs = Codecs(standIns.Script($"printf 'codec_name=%s\\n' \"$(cat '{said}')\""));

        ArtefactCodecReading before = await codecs.ReadAsync(Placed(900), CancellationToken.None);
        File.WriteAllText(said, "hevc");
        ArtefactCodecReading after = await codecs.ReadAsync(Placed(1_200), CancellationToken.None);

        Assert.Equal(EncodeCodec.H264, before.Codec);
        Assert.Equal(EncodeCodec.H265, after.Codec);
    }

    [Fact]
    public async Task AFileThatCouldNotBeReadIsAskedAgainNextTime()
    {
        string asked = standIns.Named("asked");
        IArtefactCodecReader codecs = Codecs(standIns.Script($"echo x >> '{asked}'; exit 1"));
        PlaybackFile file = Placed(900);

        await codecs.ReadAsync(file, CancellationToken.None);
        await codecs.ReadAsync(file, CancellationToken.None);

        Assert.Equal(2, File.ReadAllLines(asked).Length);
    }

    private PlaybackFile Placed(int bytes)
    {
        File.WriteAllBytes(Path.Combine(shelved.FullName, Named.Value), new byte[bytes]);

        return new PlaybackFile(Shelf, Named, bytes);
    }

    private FfprobeArtefactCodecs Codecs(string prober)
        => new(
            new LocalPlaybackFileStore(
                new IntegritySettings(),
                new EncodeSettings { OutputRoots = [new StorageRootPath(Shelf, shelved.FullName)] },
                NullLogger<LocalPlaybackFileStore>.Instance),
            new MachineSettings { Prober = prober, LongestRead = TimeSpan.FromSeconds(30) },
            TimeProvider.System);
}
