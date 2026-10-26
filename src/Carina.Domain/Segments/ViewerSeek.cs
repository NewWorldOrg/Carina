using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// What a viewer's seek was: forward past a stretch, or back into a stretch a forward seek passed moments before
/// and watched on from there.
/// </summary>
public enum SeekAction
{
    Forward = 1,

    Returned = 2,
}

/// <summary>
/// How a viewer sought: by the seek bar, by the buttons and taps that step ten seconds, by the button to the next
/// chapter, or by the keys that step ten seconds.
/// </summary>
public enum SeekMeans
{
    Bar = 1,

    Step = 2,

    Chapter = 3,

    Key = 4,
}

/// <summary>
/// One seek a viewer made in one recording: when it was taken in, whether it went forward or came back, how it was
/// made, and the two moments in the recording's own time it names. A forward seek names where the viewer was and
/// where they landed, later; steps made one after another are one seek. Coming back names where the viewer was and
/// where they came back to, earlier.
/// </summary>
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

        if (action is SeekAction.Forward ? to <= from : to >= from)
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
}
