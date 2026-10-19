namespace Carina.Domain.Segments;

/// <summary>
/// A stretch as the file playback plays has it, and whether a button offers to skip it.
/// </summary>
public sealed record PlaybackChapter(SegmentSpan Stretch, bool Skippable)
{
    /// <summary>
    /// The chapters of <paramref name="stretches"/> on the file <paramref name="clock"/> reads, leaving out a
    /// stretch the file does not play. A chapter can be skipped when it is a break, an opening or an ending, it
    /// is at least <paramref name="threshold"/> likely or carries no probability to weigh, and another chapter
    /// follows it.
    /// </summary>
    public static IReadOnlyList<PlaybackChapter> Of(IReadOnlyList<SegmentSpan> stretches, PlaybackFileClock clock, double threshold)
    {
        ArgumentNullException.ThrowIfNull(stretches);
        ArgumentNullException.ThrowIfNull(clock);

        if (!(threshold is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "A threshold is a probability, from 0 to 1.");
        }

        SegmentSpan[] played = [.. stretches.Select(clock.Placed).OfType<SegmentSpan>()];

        return [.. played.Select((stretch, at) => new PlaybackChapter(stretch, at < played.Length - 1 && Skips(stretch, threshold)))];
    }

    private static bool Skips(SegmentSpan stretch, double threshold)
        => stretch.Kind is not SegmentKind.Programme && (stretch.Probability is not { } probability || probability >= threshold);
}
