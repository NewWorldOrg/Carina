using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What the run that watches the picture for a station's watermark saw, one picture at a time and
/// holding none of them: each picture is handed to the learner, and to the mark learned ahead when
/// there is one, and the moment each was shown at is read off the line the filter that describes it
/// wrote. The pictures and the lines arrive on separate streams in the same order, one line for each
/// picture, so the n-th moment belongs to the n-th picture; whatever one stream got further with at
/// the end of a run is left unmatched rather than guessed at.
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
