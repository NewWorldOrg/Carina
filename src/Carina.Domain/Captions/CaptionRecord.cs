namespace Carina.Domain.Captions;

/// <summary>
/// The captions taken from one recording's file: the canvas they were drawn on, where the file's own
/// clock begins as it was read when they were taken, every change in the order ffmpeg drew it, and every
/// change of their text, or no text at all for a record kept before the text was taken.
/// </summary>
public sealed record CaptionRecord
{
    public CaptionRecord(int width, int height, TimeSpan startsAt, IReadOnlyList<CaptionCue> cues, IReadOnlyList<CaptionLine>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(cues);

        if (width < 1 || width > CaptionPlacement.FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A canvas is measured in two bytes a side.");
        }

        if (height < 1 || height > CaptionPlacement.FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A canvas is measured in two bytes a side.");
        }

        Width = width;
        Height = height;
        StartsAt = startsAt;
        Cues = cues;
        Lines = lines;
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan StartsAt { get; }

    public IReadOnlyList<CaptionCue> Cues { get; }

    public IReadOnlyList<CaptionLine>? Lines { get; }

    public int Pictures => Cues.Count(cue => !cue.Clears);
}
