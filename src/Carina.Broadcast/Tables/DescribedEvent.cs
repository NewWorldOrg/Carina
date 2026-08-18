using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.Tables;

public sealed class DescribedEvent(
    int eventId,
    DateTimeOffset startsAt,
    TimeSpan? runs,
    RunningStatus status,
    bool isScrambled,
    IReadOnlyList<Descriptor> descriptors)
{
    public int EventId { get; } = eventId;

    public DateTimeOffset StartsAt { get; } = startsAt;

    public TimeSpan? Runs { get; } = runs;

    public RunningStatus Status { get; } = status;

    public bool IsScrambled { get; } = isScrambled;

    public IReadOnlyList<Descriptor> Descriptors { get; } = descriptors;

    public DateTimeOffset? EndsAt => Runs is { } runs ? StartsAt + runs : null;

    public ShortEventDescription? Described
    {
        get
        {
            foreach (var descriptor in Descriptors)
            {
                if (ShortEventDescription.TryRead(descriptor, out var described))
                {
                    return described;
                }
            }

            return null;
        }
    }

    public ExtendedEventDescription? Detailed
        => ExtendedEventDescription.TryRead(Descriptors, out var detailed) ? detailed : null;

    public IReadOnlyList<ContentGenre> Genres
    {
        get
        {
            foreach (var descriptor in Descriptors)
            {
                if (ContentGenres.TryRead(descriptor, out var genres))
                {
                    return genres;
                }
            }

            return [];
        }
    }

    public IReadOnlyList<ComponentDescription> Components => [.. Read<ComponentDescription>(ComponentDescription.TryRead)];

    public IReadOnlyList<AudioComponentDescription> AudioComponents => [.. Read<AudioComponentDescription>(AudioComponentDescription.TryRead)];

    public IReadOnlyList<EventGrouping> Groupings => [.. Read<EventGrouping>(EventGrouping.TryRead)];

    public IReadOnlyList<DataContentDescription> DataContents
        => [.. Read<DataContentDescription>(DataContentDescription.TryRead)];

    private delegate bool Reads<T>(Descriptor descriptor, out T? read)
        where T : class;

    private IEnumerable<T> Read<T>(Reads<T> reads)
        where T : class
    {
        foreach (var descriptor in Descriptors)
        {
            if (reads(descriptor, out var read))
            {
                yield return read!;
            }
        }
    }
}
