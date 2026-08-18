using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.Tables;

public sealed record DescribedEvent(
    int EventId,
    DateTimeOffset StartsAt,
    TimeSpan? Runs,
    RunningStatus Status,
    bool IsScrambled,
    IReadOnlyList<Descriptor> Descriptors)
{
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
}
