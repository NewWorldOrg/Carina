namespace Carina.Domain.Captions;

public sealed record PlacedCaption(TimeSpan At, CaptionPlacement? Picture);

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
