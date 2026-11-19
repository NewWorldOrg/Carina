namespace Carina.Domain.Segments;

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
