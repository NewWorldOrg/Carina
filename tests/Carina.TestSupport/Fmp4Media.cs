using System.Buffers.Binary;
using System.Text;

namespace Carina.TestSupport;

/// <summary>
/// Where a fragment says whether a sample is a keyframe: in the run, for its first sample alone or for every sample,
/// as a default of the fragment, or as a default of the track the initialisation declares.
/// </summary>
public enum SampleFlagsCarried
{
    FirstInTheRun = 1,

    EveryInTheRun = 2,

    DefaultOfTheFragment = 3,

    DefaultOfTheTrack = 4,
}

/// <summary>
/// Where a fragment says how long its samples last: for every sample in the run, as a default of the fragment, or as a
/// default of the track the initialisation declares.
/// </summary>
public enum SampleDurationsCarried
{
    EveryInTheRun = 1,

    DefaultOfTheFragment = 2,

    DefaultOfTheTrack = 3,
}

/// <summary>
/// Fragmented MP4 the way the live transcoder writes it: an initialisation declaring a picture track and a sound
/// track, and fragments of a movie fragment box and its media, with only the boxes a reader of either looks at.
/// </summary>
public static class Fmp4Media
{
    public const uint PictureTrack = 1;

    public const uint SoundTrack = 2;

    public const uint SyncSample = 0x02000000;

    public const uint NotASyncSample = 0x01010000;

    public static byte[] Initialisation(
        uint pictureTimescale = 30_000,
        byte profile = 0x64,
        byte compatibility = 0x00,
        byte level = 0x28,
        bool withSound = true,
        uint trackDefaultDuration = 0,
        uint trackDefaultFlags = 0,
        string pictureEntry = "avc1")
        => Fmp4.Joined(
            Box("ftyp", Fmp4.Joined(Ascii("isom"), new byte[4], Ascii("isomiso6mp41"))),
            Box("moov", Fmp4.Joined(
                Box("mvhd", new byte[100]),
                PictureTrak(pictureTimescale, profile, compatibility, level, pictureEntry),
                withSound ? SoundTrak() : [],
                Box("mvex", Fmp4.Joined(
                    Trex(PictureTrack, trackDefaultDuration, trackDefaultFlags),
                    withSound ? Trex(SoundTrack, 1024, SyncSample) : [])))));

    public const int SampleLength = 16;

    public static byte[] Fragment(
        ulong decodeTime,
        IReadOnlyList<uint> pictureDurations,
        bool startsWithAKeyframe,
        SampleFlagsCarried flags = SampleFlagsCarried.FirstInTheRun,
        SampleDurationsCarried durations = SampleDurationsCarried.EveryInTheRun,
        bool withSound = true,
        int keyframeAt = 0,
        int soundSamples = 2)
    {
        ArgumentNullException.ThrowIfNull(pictureDurations);

        int sounds = withSound ? soundSamples : 0;
        byte[] Moof(int offset) => Box("moof", Fmp4.Joined(
            Box("mfhd", new byte[8]),
            withSound ? SoundTraf(decodeTime * 48 / 30, offset, sounds) : [],
            PictureTraf(decodeTime, pictureDurations, startsWithAKeyframe, flags, durations, offset + (sounds * SampleLength), keyframeAt)));
        int length = Moof(0).Length;

        return Fmp4.Joined(Moof(length + 8), Box("mdat", Media(sounds, pictureDurations.Count)));
    }

    public static byte[] SoundAlone(ulong decodeTime)
    {
        byte[] Moof(int offset) => Box("moof", Fmp4.Joined(Box("mfhd", new byte[8]), SoundTraf(decodeTime, offset, 2)));
        int length = Moof(0).Length;

        return Fmp4.Joined(Moof(length + 8), Box("mdat", Media(2, 0)));
    }

    /// <summary>
    /// The bytes of a picture sample, each sample's own, so that a fragment cut in two can be told apart.
    /// </summary>
    public static byte[] PictureSample(int sample) => [.. Enumerable.Repeat((byte)(0x80 + (sample % 64)), SampleLength)];

    public static byte[] SoundSample() => [.. Enumerable.Repeat((byte)0x11, SampleLength)];

    private static byte[] Media(int sounds, int pictures)
        => Fmp4.Joined(
        [
            .. Enumerable.Range(0, sounds).Select(_ => SoundSample()),
            .. Enumerable.Range(0, pictures).Select(PictureSample),
        ]);

    public static byte[] Box(string kind, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(body);

        byte[] box = new byte[8 + body.Length];

        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Words(params uint[] words)
    {
        byte[] written = new byte[words.Length * 4];

        for (int at = 0; at < words.Length; at++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(written.AsSpan(at * 4), words[at]);
        }

        return written;
    }

    private static byte[] Tkhd(uint track) => Fmp4.Joined(Words(0x00000003, 0, 0, track, 0, 0), new byte[60]);

    private static byte[] Mdhd(uint timescale) => Fmp4.Joined(Words(0, 0, 0, timescale, 0), new byte[4]);

    private static byte[] Hdlr(string handler) => Fmp4.Joined(Words(0, 0), Ascii(handler), new byte[13]);

    private static byte[] PictureTrak(uint timescale, byte profile, byte compatibility, byte level, string entry)
        => Box("trak", Fmp4.Joined(
            Box("tkhd", Tkhd(PictureTrack)),
            Box("mdia", Fmp4.Joined(
                Box("mdhd", Mdhd(timescale)),
                Box("hdlr", Hdlr("vide")),
                Box("minf", Box("stbl", Box("stsd", Fmp4.Joined(Words(0, 1), PictureEntry(entry, profile, compatibility, level)))))))));

    private static byte[] PictureEntry(string entry, byte profile, byte compatibility, byte level)
        => Box(entry, Fmp4.Joined(
            new byte[78],
            Box(entry is "avc1" or "avc3" ? "avcC" : "hvcC", [1, profile, compatibility, level, 0xFF, 0xE0])));

    private static byte[] SoundTrak()
        => Box("trak", Fmp4.Joined(
            Box("tkhd", Tkhd(SoundTrack)),
            Box("mdia", Fmp4.Joined(
                Box("mdhd", Mdhd(48_000)),
                Box("hdlr", Hdlr("soun")),
                Box("minf", Box("stbl", Box("stsd", Fmp4.Joined(
                    Words(0, 1),
                    Box("mp4a", Fmp4.Joined(new byte[28], Box("esds", new byte[24])))))))))));

    private static byte[] Trex(uint track, uint duration, uint flags) => Box("trex", Words(0, track, 1, duration, 0, flags));

    private static byte[] SoundTraf(ulong decodeTime, int offset, int samples)
        => Box("traf", Fmp4.Joined(
            Box("tfhd", Words(0x00020000, SoundTrack)),
            Box("tfdt", Fmp4.Joined(Words(0x01000000), Wide(decodeTime))),
            Box("trun", Words(
            [
                0x00000301,
                (uint)samples,
                (uint)offset,
                .. Enumerable.Range(0, samples).SelectMany(_ => (uint[])[1024, SampleLength]),
            ]))));

    private static byte[] PictureTraf(
        ulong decodeTime,
        IReadOnlyList<uint> durations,
        bool startsWithAKeyframe,
        SampleFlagsCarried flags,
        SampleDurationsCarried carried,
        int offset,
        int keyframeAt)
    {
        uint first = startsWithAKeyframe && keyframeAt is 0 ? SyncSample : NotASyncSample;
        List<uint> tfhd = [0x00020000, PictureTrack];
        uint run = 0x00000201;

        if (carried is SampleDurationsCarried.DefaultOfTheFragment)
        {
            tfhd[0] |= 0x00000008;
            tfhd.Add(durations.Count is 0 ? 0 : durations[0]);
        }

        if (flags is SampleFlagsCarried.DefaultOfTheFragment or SampleFlagsCarried.FirstInTheRun)
        {
            tfhd[0] |= 0x00000020;
            tfhd.Add(flags is SampleFlagsCarried.FirstInTheRun ? NotASyncSample : first);
        }

        if (carried is SampleDurationsCarried.EveryInTheRun)
        {
            run |= 0x00000100;
        }

        if (flags is SampleFlagsCarried.FirstInTheRun)
        {
            run |= 0x00000004;
        }

        if (flags is SampleFlagsCarried.EveryInTheRun)
        {
            run |= 0x00000400;
        }

        List<uint> trun = [run, (uint)durations.Count, (uint)offset];

        if (flags is SampleFlagsCarried.FirstInTheRun)
        {
            trun.Add(first);
        }

        for (int at = 0; at < durations.Count; at++)
        {
            if (carried is SampleDurationsCarried.EveryInTheRun)
            {
                trun.Add(durations[at]);
            }

            trun.Add(SampleLength);

            if (flags is SampleFlagsCarried.EveryInTheRun)
            {
                trun.Add(Said(at, first, keyframeAt));
            }
        }

        return Box("traf", Fmp4.Joined(
            Box("tfhd", Words([.. tfhd])),
            Box("tfdt", Fmp4.Joined(Words(0x01000000), Wide(decodeTime))),
            Box("trun", Words([.. trun]))));
    }

    private static uint Said(int sample, uint first, int keyframeAt)
    {
        if (sample is 0)
        {
            return first;
        }

        return keyframeAt > 0 && sample == keyframeAt ? SyncSample : NotASyncSample;
    }

    private static byte[] Wide(ulong value)
    {
        byte[] written = new byte[8];

        BinaryPrimitives.WriteUInt64BigEndian(written, value);

        return written;
    }
}
