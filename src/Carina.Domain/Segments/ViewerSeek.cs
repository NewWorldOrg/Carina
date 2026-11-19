using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public enum SeekAction
{
    Forward = 1,

    Returned = 2,
}

public enum SeekMeans
{
    Bar = 1,

    Step = 2,

    Chapter = 3,

    Key = 4,
}

public sealed record ViewerSeek
{
    public ViewerSeek(DateTime happenedAt, SeekAction action, SeekMeans means, TimeSpan from, TimeSpan to)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "A viewer seeks forward or comes back.");
        }

        if (!Enum.IsDefined(means))
        {
            throw new ArgumentOutOfRangeException(nameof(means), means, "A seek is made by the bar, a step, a chapter or a key.");
        }

        if (from < TimeSpan.Zero || to < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "A seek names moments no earlier than the recording's own zero.");
        }

        if (!GoesItsWay(action, from, to))
        {
            throw new ArgumentException("A seek forward lands later than where it started, and coming back earlier.", nameof(to));
        }

        HappenedAt = UtcTimes.Required(happenedAt, nameof(happenedAt));
        Action = action;
        Means = means;
        From = from;
        To = to;
    }

    public DateTime HappenedAt { get; }

    public SeekAction Action { get; }

    public SeekMeans Means { get; }

    public TimeSpan From { get; }

    public TimeSpan To { get; }

    public static bool GoesItsWay(SeekAction action, TimeSpan from, TimeSpan to) => action is SeekAction.Forward ? to > from : to < from;
}
