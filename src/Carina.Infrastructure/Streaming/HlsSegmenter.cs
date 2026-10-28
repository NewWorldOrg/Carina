using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Cuts what a transcoder writes into segments that each begin with a keyframe, cutting a fragment in two where a
/// keyframe falls inside it.
/// </summary>
public sealed class HlsSegmenter
{
    private const int AVisualEntryBeforeItsBoxes = 78;

    private const string Aac = "mp4a.40.2";

    private readonly HlsSettings settings;

    private readonly HlsPlaylistType type;

    private readonly List<HlsSegment> listed = [];

    private readonly Queue<HeldSegment> held = new();

    private readonly List<ReadOnlyMemory<byte>> open = [];

    private readonly Dictionary<uint, Fmp4TrackDefaults> tracks = [];

    private uint? picture;

    private ReadOnlyMemory<byte>? initialisation;

    private string? codecs;

    private ulong openTicks;

    private long next;

    private int target;

    private bool ended;

    public HlsSegmenter(HlsSettings settings, HlsPlaylistType type = HlsPlaylistType.Sliding)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "A playlist slides or lists every segment from the first.");
        }

        this.settings = settings;
        this.type = type;
    }

    public ReadOnlyMemory<byte>? Initialisation => initialisation;

    public HlsWindow Window => new([.. listed], target, ended, codecs, type);

    public bool HoldsASegment => listed.Count > 0;

    /// <summary>
    /// How many segments have been cut since the first.
    /// </summary>
    public long Cut => next;

    public ReadOnlyMemory<byte>? Segment(long number)
        => held.FirstOrDefault(segment => segment.Segment.Number == number)?.Bytes;

    /// <summary>
    /// Lets go of the bytes of the segments numbered before <paramref name="number"/>, which an event's playlist goes on
    /// listing.
    /// </summary>
    public void ForgetBefore(long number)
    {
        while (held.Count > 0 && held.Peek().Segment.Number < number)
        {
            held.Dequeue();
        }
    }

    public void Take(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        switch (frame.Channel)
        {
            case LiveChannel.PictureHeader:
                Initialise(frame.Payload);
                break;
            case LiveChannel.Picture:
                Add(frame.Payload);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Says the transcoder wrote to its end, and closes the segment left open with the last of what it wrote.
    /// </summary>
    public void Finish()
    {
        if (!ended && open.Count > 0 && picture is { } track)
        {
            Close(track);
        }

        End();
    }

    /// <summary>
    /// Says no more is coming, and drops the segment left open, which no keyframe closed.
    /// </summary>
    public void End()
    {
        ended = true;
        open.Clear();
        openTicks = 0;
    }

    private static uint? Word(ReadOnlySpan<byte> box, int at)
        => at >= 0 && at + 4 <= box.Length ? BinaryPrimitives.ReadUInt32BigEndian(box[at..]) : null;

    /// <summary>
    /// The number a track header or media header carries after its two dates, which take four bytes each in the
    /// first version of the box and eight in the second.
    /// </summary>
    private static uint? AfterTheDates(ReadOnlySpan<byte> box) => box.Length is 0 ? null : Word(box, box[0] is 1 ? 20 : 12);

    private static bool Handles(ReadOnlySpan<byte> mdia, ReadOnlySpan<byte> handler)
    {
        ReadOnlySpan<byte> hdlr = IsoBoxWalk.Inside(mdia, "hdlr"u8);

        return hdlr.Length >= 12 && hdlr[8..12].SequenceEqual(handler);
    }

    private static ReadOnlySpan<byte> Descriptions(ReadOnlySpan<byte> mdia)
    {
        ReadOnlySpan<byte> stsd = IsoBoxWalk.Inside(IsoBoxWalk.Inside(IsoBoxWalk.Inside(mdia, "minf"u8), "stbl"u8), "stsd"u8);

        return stsd.Length > 8 ? stsd[8..] : [];
    }

    /// <summary>
    /// The picture's codec as a playlist names it, from the profile, compatibility and level its configuration
    /// carries, or nothing for a picture in a codec this does not name.
    /// </summary>
    private static string? PictureCodec(ReadOnlySpan<byte> mdia)
    {
        var walk = new IsoBoxWalk(Descriptions(mdia));

        if (!walk.Next() || !(walk.Named("avc1"u8) || walk.Named("avc3"u8)) || walk.Body.Length < AVisualEntryBeforeItsBoxes)
        {
            return null;
        }

        string entry = Encoding.ASCII.GetString(walk.Name);
        ReadOnlySpan<byte> configuration = IsoBoxWalk.Inside(walk.Body[AVisualEntryBeforeItsBoxes..], "avcC"u8);

        return configuration.Length < 4
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{entry}.{configuration[1]:x2}{configuration[2]:x2}{configuration[3]:x2}");
    }

    private static string? SoundCodec(ReadOnlySpan<byte> mdia)
    {
        var walk = new IsoBoxWalk(Descriptions(mdia));

        return walk.Next() && walk.Named("mp4a"u8) ? Aac : null;
    }

    private static Fmp4TrackDefaults Declared(ReadOnlySpan<byte> moov, uint track, uint timescale)
    {
        var walk = new IsoBoxWalk(IsoBoxWalk.Inside(moov, "mvex"u8));

        while (walk.Next())
        {
            if (walk.Named("trex"u8) && Word(walk.Body, 4) == track)
            {
                return new Fmp4TrackDefaults(
                    timescale,
                    Word(walk.Body, 12) ?? 0U,
                    Word(walk.Body, 16) ?? 0U,
                    Word(walk.Body, 20) ?? 0U);
            }
        }

        return new Fmp4TrackDefaults(timescale, 0U, 0U, 0U);
    }

    private static int Rounded(TimeSpan length)
        => Math.Max(1, (int)Math.Round(length.TotalSeconds, MidpointRounding.AwayFromZero));

    private static int? FirstKeyframe(Fmp4Run? pictures)
    {
        if (pictures is null)
        {
            return null;
        }

        int found = pictures.Samples.ToList().FindIndex(sample => sample.IsASync);

        return found < 0 ? null : found;
    }

    private void Initialise(ReadOnlyMemory<byte> bytes)
    {
        if (initialisation is { } already)
        {
            if (!already.Span.SequenceEqual(bytes.Span))
            {
                End();
            }

            return;
        }

        ReadOnlySpan<byte> moov = IsoBoxWalk.Inside(bytes.Span, "moov"u8);
        var walk = new IsoBoxWalk(moov);
        List<string?> named = [];

        while (walk.Next())
        {
            if (walk.Named("trak"u8))
            {
                Learn(moov, walk.Body, named);
            }
        }

        if (picture is null)
        {
            tracks.Clear();

            return;
        }

        initialisation = bytes;
        codecs = named.Count > 0 && named.All(codec => codec is not null) ? string.Join(',', named) : null;
    }

    private void Learn(ReadOnlySpan<byte> moov, ReadOnlySpan<byte> trak, List<string?> named)
    {
        ReadOnlySpan<byte> mdia = IsoBoxWalk.Inside(trak, "mdia"u8);
        uint? id = AfterTheDates(IsoBoxWalk.Inside(trak, "tkhd"u8));
        uint? timescale = AfterTheDates(IsoBoxWalk.Inside(mdia, "mdhd"u8));

        if (id is not { } track || timescale is not { } ticks || ticks is 0U)
        {
            return;
        }

        tracks[track] = Declared(moov, track, ticks);

        if (Handles(mdia, "soun"u8))
        {
            named.Add(SoundCodec(mdia));

            return;
        }

        if (Handles(mdia, "vide"u8) && picture is null)
        {
            picture = track;
            named.Insert(0, PictureCodec(mdia));
        }
    }

    private void Add(ReadOnlyMemory<byte> fragment)
    {
        if (ended || picture is not { } track || Fmp4Fragment.Read(fragment, tracks) is not { } read)
        {
            return;
        }

        Fmp4Run? pictures = read.Of(track);
        ulong ticks = pictures?.Ticks ?? 0UL;

        if (FirstKeyframe(pictures) is not { } keyframe)
        {
            if (open.Count > 0)
            {
                Append(fragment, ticks);
            }

            return;
        }

        ulong before = pictures!.TicksBefore(keyframe);
        bool closes = open.Count > 0 && Length(openTicks + before, track) >= settings.ShortestSegment;

        if (!closes && open.Count > 0)
        {
            Append(fragment, ticks);

            return;
        }

        Begin(fragment, read, track, keyframe, closes);
    }

    /// <summary>
    /// Begins a segment at the keyframe, closing the one open with what was decoded before it when
    /// <paramref name="closes"/>, and dropping that when no segment was open.
    /// </summary>
    private void Begin(ReadOnlyMemory<byte> fragment, Fmp4Fragment read, uint track, int keyframe, bool closes)
    {
        Fmp4Run pictures = read.Of(track)!;
        (Fmp4Fragment before, Fmp4Fragment from) = read.CutAt(track, keyframe, tracks);
        ulong ticksBefore = pictures.TicksBefore(keyframe);

        if (closes)
        {
            if (before.Runs.Count > 0)
            {
                Append(before.Written(), ticksBefore);
            }

            Close(track);
        }

        open.Clear();
        openTicks = 0;
        Append(before.Runs.Count is 0 ? fragment : from.Written(), pictures.Ticks - ticksBefore);
    }

    private void Append(ReadOnlyMemory<byte> fragment, ulong ticks)
    {
        open.Add(fragment);
        openTicks += ticks;
    }

    private TimeSpan Length(ulong ticks, uint track)
        => TimeSpan.FromTicks((long)((UInt128)ticks * TimeSpan.TicksPerSecond / tracks[track].Timescale));

    private void Close(uint track)
    {
        TimeSpan length = Length(openTicks, track);
        byte[] joined = new byte[open.Sum(fragment => fragment.Length)];
        int at = 0;

        foreach (ReadOnlyMemory<byte> fragment in open)
        {
            fragment.Span.CopyTo(joined.AsSpan(at));
            at += fragment.Length;
        }

        open.Clear();
        openTicks = 0;

        if (length <= TimeSpan.Zero || joined.Length is 0)
        {
            return;
        }

        HlsSegment segment = new(next++, length, joined.Length);

        listed.Add(segment);
        held.Enqueue(new HeldSegment(segment, joined));
        target = Math.Max(target, Rounded(length));

        while (type is HlsPlaylistType.Sliding && held.Count > settings.SegmentsHeld)
        {
            held.Dequeue();
            listed.RemoveAt(0);
        }
    }

    private sealed record HeldSegment(HlsSegment Segment, ReadOnlyMemory<byte> Bytes);
}
