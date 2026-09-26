using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What the run that watches the picture for a station's watermark saw, one picture at a time and
/// holding none of them. Each picture is handed to the learner and to the mark learned ahead, and
/// is paired in order with the moment read off the filter's line for it; whatever one stream got
/// further with at the end of a run is left unmatched.
/// </summary>
public sealed class WatermarkWatch(WatermarkMask? learnedAhead)
{
    private readonly WatermarkLearner learner = new();

    private readonly List<bool> seen = [];

    private readonly List<TimeSpan> shown = [];

    public int Pictures => learner.Frames;

    public void Pictured(byte[] picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        learner.Pictured(picture);

        if (learnedAhead is { } mark)
        {
            seen.Add(mark.SeenIn(picture));
        }
    }

    public void Complained(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (ChapterLog.Moment(line, ChapterLog.Pictured) is { } at)
        {
            shown.Add(at);
        }
    }

    public WatermarkMask? Learned() => learner.Learned();

    public IReadOnlyList<WatermarkSighting> Sightings(EncodeTimeline timeline, TimeSpan artefactLength)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        List<WatermarkSighting> placed = [];
        int matched = Math.Min(seen.Count, shown.Count);

        for (int picture = 0; picture < matched; picture++)
        {
            if (ChapterClock.OnTheArtefact(shown[picture], timeline.SourceStart, timeline.HeadSkip, artefactLength) is { } at)
            {
                placed.Add(new WatermarkSighting(at, seen[picture]));
            }
        }

        return placed;
    }
}
