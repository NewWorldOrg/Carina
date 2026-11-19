namespace Carina.Domain.Segments;

public sealed record PlaybackChapter(SegmentSpan Stretch, bool Skippable)
{
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
