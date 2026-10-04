using System.Runtime.Versioning;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Playback;
using Carina.Infrastructure.Tests.Integrity;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Playback;

/// <summary>
/// Writes a synthetic H.265 picture into an mp4 with the ffmpeg the application runs, the way an encode
/// tags it and the way ffmpeg would on its own, puts a text track of captions into it as an artefact
/// already made has one put in, and reads each file back the way playback does before choosing what to
/// hand over.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class HevcTagMaterialTests : IDisposable
{
    /// <summary>
    /// Two black 64x64 frames in H.265 as a raw stream, made once with x265. The ffmpeg the application
    /// runs has no H.265 encoder on the processor, so the picture is carried here and only copied.
    /// </summary>
    private const string TwoBlackFrames =
        "AAAAAUABDAH//wQIAAADAJ+oAAADAAAeugJAAAAAAUIBAQQIAAADAJ+oAAADAAAeoCCBBZbpKTC8BaAgAAADACAAAAMDIQAAAAFE"
        + "AcBxgRIAAAEoAa3g0Rf/05FzI4uAAAAAAUABDAH//wQIAAADAJ+oAAADAAAeugJAAAAAAUIBAQQIAAADAJ+oAAADAAAeoCCBBZbp"
        + "KTC8BaAgAAADACAAAAMDIQAAAAFEAcBxgRIAAAEoAawrgOrlf/4VYTx+ofg=";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Shelf = new("shelf");

    private readonly TempTree room = new();

    public void Dispose() => room.Dispose();

    [Fact(DisplayName = "an H.265 picture written the way an encode tags it is read as H.265 tagged hvc1, and handed as it is to a browser that decodes h265")]
    public async Task AnH265PictureWrittenTheWayAnEncodeTagsItIsTaggedHvc1()
    {
        PlaybackFile artefact = await WrittenAsync("tagged.mp4", FfmpegEncodeInvocation.Tagging(EncodeCodec.H265));

        ArtefactCodecReading reading = await Codecs().ReadAsync(artefact, Cancel);

        Assert.True(reading.Read, reading.Note);
        Assert.Equal((EncodeCodec.H265, "hvc1"), (reading.Codec, reading.Tag));
        Assert.True(PlaybackAudience.BrowserDecodingH265.Plays(reading, EncodeCodec.H265));
        Assert.False(PlaybackAudience.BrowserSayingNothing.Plays(reading, EncodeCodec.H265));
        Assert.True(PlaybackAudience.ExternalPlayer.Plays(reading, EncodeCodec.H265));
    }

    [Fact(DisplayName = "an H.265 picture ffmpeg tags on its own is read as hev1, which a browser that decodes h265 is not handed and an external player is")]
    public async Task AnH265PictureFfmpegTagsOnItsOwnIsTaggedHev1()
    {
        PlaybackFile artefact = await WrittenAsync("untagged.mp4", []);

        ArtefactCodecReading reading = await Codecs().ReadAsync(artefact, Cancel);

        Assert.True(reading.Read, reading.Note);
        Assert.Equal((EncodeCodec.H265, "hev1"), (reading.Codec, reading.Tag));
        Assert.False(PlaybackAudience.BrowserDecodingH265.Plays(reading, EncodeCodec.H265));
        Assert.True(PlaybackAudience.ExternalPlayer.Plays(reading, EncodeCodec.H265));
    }

    [Fact(DisplayName = "putting a text track of captions into an artefact tagged hvc1 keeps its picture tagged hvc1")]
    public async Task PuttingATextTrackIntoAnArtefactTaggedHvc1KeepsItsTag()
    {
        PlaybackFile artefact = await WrittenAsync("tagged.mp4", FfmpegEncodeInvocation.Tagging(EncodeCodec.H265));
        string captions = room.Under("captions.vtt");
        await File.WriteAllTextAsync(captions, "WEBVTT\n\n00:00:00.000 --> 00:00:00.040\nこんにちは\n", Cancel);
        string captioned = room.Under("captioned.mp4");

        await SayAsync("ffmpeg", FfmpegCaptionTrackInvocation.Arguments(room.Under(artefact.Name.Value), captions, captioned));
        ArtefactCodecReading reading = await Codecs().ReadAsync(
            new PlaybackFile(Shelf, new RecordingFileName("captioned.mp4"), new FileInfo(captioned).Length),
            Cancel);

        Assert.Contains("subtitle", await SayAsync("ffprobe", FfmpegCaptionTrackInvocation.Carried(captioned)), StringComparison.Ordinal);
        Assert.Equal((EncodeCodec.H265, "hvc1"), (reading.Codec, reading.Tag));
    }

    private async Task<PlaybackFile> WrittenAsync(string name, IReadOnlyList<string> tagging)
    {
        string stream = room.Under("two-black-frames.hevc");
        await File.WriteAllBytesAsync(stream, Convert.FromBase64String(TwoBlackFrames), Cancel);
        string destination = room.Under(name);

        await SayAsync(
            "ffmpeg",
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-f",
                "hevc",
                "-i",
                stream,
                "-c:v",
                "copy",
                .. tagging,
                .. FfmpegEncodeInvocation.Delivery(destination),
            ]);

        return new PlaybackFile(Shelf, new RecordingFileName(name), new FileInfo(destination).Length);
    }

    private FfprobeArtefactCodecs Codecs()
        => new(
            new LocalPlaybackFileStore(
                new IntegritySettings(),
                new EncodeSettings { OutputRoots = [new StorageRootPath(Shelf, room.Root)] },
                NullLogger<LocalPlaybackFileStore>.Instance),
            new MachineSettings(),
            TimeProvider.System);

    private static async Task<string> SayAsync(string programme, IReadOnlyList<string> arguments)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(programme, arguments, TimeSpan.FromMinutes(1), TimeProvider.System, Cancel);

        Assert.True(said.ExitCode is 0, said.Complained);

        return said.Said;
    }
}
