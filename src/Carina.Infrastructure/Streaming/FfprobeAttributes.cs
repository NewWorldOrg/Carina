using Carina.Domain.Streaming;

using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Streaming;

public static class FfprobeAttributes
{
    private const string CodecType = "codec_type";
    private const string Video = "video";
    private const string Audio = "audio";
    private const string Width = "width";
    private const string Height = "height";
    private const string FieldOrder = "field_order";
    private const string FrameRate = "r_frame_rate";
    private const string Channels = "channels";
    private const string ChannelLayout = "channel_layout";
    private const string Stereo = "stereo";

    public static StreamAttributeReading Read(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<FfprobeRecord> records = FfprobeRecords.From(output);
        IReadOnlyList<FfprobeRecord> pictures = [.. records.Where(record => record.Value(CodecType) is Video)];
        FfprobeRecord? picture = pictures.FirstOrDefault();
        FfprobeRecord? sound = records.FirstOrDefault(record => record.Value(CodecType) is Audio);

        if (picture is null && sound is null)
        {
            return StreamAttributeReading.Unanswered(
                StreamProbeFault.SaidNothing,
                "the programme exited 0 and named no stream of either kind");
        }

        List<StreamAttribute> guessed = [];

        VideoSize size = SizeIn(picture) ?? Fell(guessed, StreamAttribute.Resolution, StreamAttributes.SafeSide.Size);
        ScanType scan = ScanIn(picture) ?? Fell(guessed, StreamAttribute.Scan, StreamAttributes.SafeSide.Scan);
        FrameRate rate = RateIn(picture) ?? Fell(guessed, StreamAttribute.FrameRate, StreamAttributes.SafeSide.Rate);
        AudioMode mode = ModeIn(sound) ?? Fell(guessed, StreamAttribute.Audio, StreamAttributes.SafeSide.Audio);

        return StreamAttributeReading.Read(
            new StreamAttributes(size, scan, rate, mode),
            guessed,
            SeveralDescriptions(pictures));
    }

    private static T Fell<T>(List<StreamAttribute> guessed, StreamAttribute attribute, T safeSide)
    {
        guessed.Add(attribute);

        return safeSide;
    }

    private static bool SeveralDescriptions(IReadOnlyList<FfprobeRecord> pictures)
        => pictures
            .Select(Described)
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;

    private static string Described(FfprobeRecord picture)
        => string.Join(
            '|',
            picture.Value(Width),
            picture.Value(Height),
            picture.Value(FieldOrder),
            picture.Value(FrameRate));

    private static VideoSize? SizeIn(FfprobeRecord? picture)
    {
        if (picture is null
            || !int.TryParse(picture.Value(Width), out int width)
            || !int.TryParse(picture.Value(Height), out int height)
            || width < 1
            || height < 1)
        {
            return null;
        }

        return new VideoSize(width, height);
    }

    private static ScanType? ScanIn(FfprobeRecord? picture)
        => picture?.Value(FieldOrder) switch
        {
            "progressive" => ScanType.Progressive,
            "tt" or "bb" or "tb" or "bt" => ScanType.Interlaced,
            _ => null,
        };

    private static FrameRate? RateIn(FfprobeRecord? picture)
        => Domain.Streaming.FrameRate.Read(picture?.Value(FrameRate));

    private static AudioMode? ModeIn(FfprobeRecord? sound)
    {
        if (sound is null || !int.TryParse(sound.Value(Channels), out int channels) || channels < 1)
        {
            return null;
        }

        if (channels is 1)
        {
            return AudioMode.Mono;
        }

        if (channels > 2)
        {
            return AudioMode.Surround;
        }

        return sound.Value(ChannelLayout) is Stereo ? AudioMode.Stereo : AudioMode.DualMono;
    }
}
