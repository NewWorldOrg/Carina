namespace Carina.Broadcast.DsmCc;

public sealed class EventMessageClock
{
    public const long PtsModulus = 1L << 33;

    public const int MostWaiting = 256;

    private readonly Dictionary<int, int> versions = [];
    private readonly List<(GeneralEvent Event, long Npt)> waiting = [];

    private NptReference? reference;

    public IReadOnlyList<EventMessageOutcome> Push(StreamDescriptorSection section, long receivedPts)
    {
        ArgumentNullException.ThrowIfNull(section);

        var outcomes = new List<EventMessageOutcome>();

        foreach (NptReference arrived in section.NptReferences)
        {
            Adopt(arrived, outcomes);
        }

        if (versions.TryGetValue(section.TableIdExtension, out int seen) && seen == section.VersionNumber)
        {
            return outcomes;
        }

        versions[section.TableIdExtension] = section.VersionNumber;

        foreach (GeneralEvent carried in section.Events)
        {
            if (Time(carried, receivedPts) is { } outcome)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }

    private void Adopt(NptReference arrived, List<EventMessageOutcome> outcomes)
    {
        if (!arrived.IsUsable)
        {
            outcomes.Add(new EventMessageOutcome.Discarded(EventMessageDefect.UnusableNptReference));

            return;
        }

        reference = arrived;

        foreach ((GeneralEvent held, long npt) in waiting)
        {
            outcomes.Add(Timed(held, OnSystemClock(arrived, npt)));
        }

        waiting.Clear();
    }

    private EventMessageOutcome? Time(GeneralEvent carried, long receivedPts)
        => carried switch
        {
            { TimeMode: GeneralEvent.Immediate } => Timed(carried, Wrapped(receivedPts)),
            { Npt: long npt } when reference is not null => Timed(carried, OnSystemClock(reference, npt)),
            { Npt: long npt } => Wait(carried, npt),
            _ => new EventMessageOutcome.Discarded(EventMessageDefect.UnsupportedTimeMode),
        };

    private EventMessageOutcome.Discarded? Wait(GeneralEvent carried, long npt)
    {
        if (waiting.Count >= MostWaiting)
        {
            return new EventMessageOutcome.Discarded(EventMessageDefect.TooManyWaiting);
        }

        waiting.Add((carried, npt));

        return null;
    }

    private static EventMessageOutcome.Timed Timed(GeneralEvent carried, long firesAt)
        => new(new TimedEventMessage(
            carried.EventMessageGroupId,
            carried.EventMessageId,
            carried.EventMessageType,
            carried.TimeMode,
            firesAt,
            carried.PrivateData));

    private static long OnSystemClock(NptReference reference, long npt)
        => Wrapped(reference.Stc + ((npt - reference.Npt) * reference.ScaleDenominator / reference.ScaleNumerator));

    private static long Wrapped(long pts) => ((pts % PtsModulus) + PtsModulus) % PtsModulus;
}
