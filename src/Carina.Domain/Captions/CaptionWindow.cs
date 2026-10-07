namespace Carina.Domain.Captions;

/// <summary>
/// One change of the captions at a second of the source a recording is played from.
/// </summary>
public sealed record PlacedCaption(TimeSpan At, CaptionPlacement? Picture);

/// <summary>
/// The captions of ten minutes of a source, starting where the player asked: the caption already showing
/// there first, then every change before <see cref="Until"/>, each at the second of the source counted
/// from its own zero.
/// </summary>
public sealed record CaptionWindow
{
    public static readonly TimeSpan Covers = TimeSpan.FromMinutes(10);

    private CaptionWindow(int width, int height, TimeSpan until, IReadOnlyList<PlacedCaption> captions)
    {
        Width = width;
        Height = height;
        Until = until;
        Captions = captions;
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Until { get; }

    public IReadOnlyList<PlacedCaption> Captions { get; }

    /// <summary>
    /// Places a record on a source whose zero is <paramref name="shift"/> on the file's own clock, as
    /// <see cref="Placed"/> does, and keeps the window of it starting at <paramref name="from"/>.
    /// </summary>
    public static CaptionWindow Of(CaptionRecord record, TimeSpan shift, TimeSpan? length, TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (from < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "A window starts at or after the source's zero.");
        }

        TimeSpan until = from + Covers;
        IReadOnlyList<PlacedCaption> placed = Placed(record, shift, length);

        PlacedCaption? showing = placed.LastOrDefault(caption => caption.At <= from);
        IEnumerable<PlacedCaption> ahead = placed.Where(caption => caption.At > from && caption.At < until);

        return new CaptionWindow(
            record.Width,
            record.Height,
            until,
            [.. showing is { Picture: not null } ? [showing] : Array.Empty<PlacedCaption>(), .. ahead]);
    }

    /// <summary>
    /// Every change of a record placed on a source whose zero is <paramref name="shift"/> on the file's own
    /// clock, in order. A change before the source's zero is moved up to it, and one after
    /// <paramref name="length"/>, when the source says how long it is, is left out.
    /// </summary>
    public static IReadOnlyList<PlacedCaption> Placed(CaptionRecord record, TimeSpan shift, TimeSpan? length)
    {
        ArgumentNullException.ThrowIfNull(record);

        return
        [
            .. record.Cues
                .Select(cue => new PlacedCaption(cue.At - shift > TimeSpan.Zero ? cue.At - shift : TimeSpan.Zero, cue.Picture))
                .Where(caption => length is not { } lasts || caption.At <= lasts)
                .OrderBy(caption => caption.At),
        ];
    }
}
