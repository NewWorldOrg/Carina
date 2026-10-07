namespace Carina.Domain.Segments;

/// <summary>
/// The gaps found in the sound and in the frames of one recording, joined where they overlap or
/// touch, and handed out in order: those ending by a time nothing found later can start before,
/// and at the end the rest. A stretch shorter than <see cref="Shortest"/> that had to be filled is
/// not a gap.
/// </summary>
public sealed class LearningDataGaps
{
    public static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(100);

    private readonly List<LearningDataGap> held = [];

    public void Add(LearningDataGap? gap)
    {
        if (gap is null)
        {
            return;
        }

        int at = 0;

        while (at < held.Count && held[at].Until < gap.From)
        {
            at++;
        }

        TimeSpan from = gap.From;
        TimeSpan until = gap.Until;

        while (at < held.Count && held[at].From <= until)
        {
            from = from < held[at].From ? from : held[at].From;
            until = until > held[at].Until ? until : held[at].Until;
            held.RemoveAt(at);
        }

        held.Insert(at, new LearningDataGap(from, until));
    }

    public IReadOnlyList<LearningDataGap> TakeThrough(TimeSpan through)
    {
        int ended = 0;

        while (ended < held.Count && held[ended].Until <= through)
        {
            ended++;
        }

        LearningDataGap[] taken = [.. held.Take(ended)];
        held.RemoveRange(0, ended);

        return taken;
    }

    public IReadOnlyList<LearningDataGap> TakeAll() => TakeThrough(TimeSpan.MaxValue);
}
