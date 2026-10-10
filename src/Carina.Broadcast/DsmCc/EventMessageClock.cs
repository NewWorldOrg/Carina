namespace Carina.Broadcast.DsmCc;

public sealed class EventMessageClock
{
    public const long PtsModulus = 1L << 33;

    public const int MostWaiting = 256;

    private readonly Dictionary<SectionKey, int> versions = [];
    private readonly List<(SectionKey Section, GeneralEvent Event, long Npt)> waiting = [];

    private NptReference? reference;

    public IReadOnlyList<EventMessageOutcome> Push(StreamDescriptorSection section, long receivedPts)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (!section.IsCurrent)
        {
            return [];
        }

        SectionKey key = new(section.TableIdExtension, section.SectionNumber);

        if (versions.TryGetValue(key, out int seen) && seen == section.VersionNumber)
        {
            return [];
        }

        versions[key] = section.VersionNumber;
        var outcomes = new List<EventMessageOutcome>();

        Supersede(key, outcomes);

        foreach (NptReference arrived in section.NptReferences)
        {
            Adopt(arrived, outcomes);
        }

        foreach (GeneralEvent carried in section.Events)
        {
            if (Time(key, carried, receivedPts) is { } outcome)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }

    public void Reset()
    {
        versions.Clear();
        waiting.Clear();
        reference = null;
    }

    private void Supersede(SectionKey key, List<EventMessageOutcome> outcomes)
    {
        int superseded = waiting.RemoveAll(held => held.Section == key);

        for (int index = 0; index < superseded; index++)
        {
            outcomes.Add(new EventMessageOutcome.Discarded(EventMessageDefect.Superseded));
        }
    }

    private void Adopt(NptReference arrived, List<EventMessageOutcome> outcomes)
    {
        if (!arrived.IsUsable)
        {
            outcomes.Add(new EventMessageOutcome.Discarded(EventMessageDefect.UnusableNptReference));

            return;
        }

        reference = arrived;

        foreach ((_, GeneralEvent held, long npt) in waiting)
        {
            outcomes.Add(Timed(held, OnSystemClock(arrived, npt)));
        }

        waiting.Clear();
    }

    private EventMessageOutcome? Time(SectionKey key, GeneralEvent carried, long receivedPts)
        => carried switch
        {
            { TimeMode: GeneralEvent.Immediate } => Timed(carried, Wrapped(receivedPts)),
            { Npt: long npt } when reference is not null => Timed(carried, OnSystemClock(reference, npt)),
            { Npt: long npt } => Wait(key, carried, npt),
            _ => new EventMessageOutcome.Discarded(EventMessageDefect.UnsupportedTimeMode),
        };

    private EventMessageOutcome.Discarded? Wait(SectionKey key, GeneralEvent carried, long npt)
    {
        if (waiting.Count >= MostWaiting)
        {
            return new EventMessageOutcome.Discarded(EventMessageDefect.TooManyWaiting);
        }

        waiting.Add((key, carried, npt));

        return null;
    }

    private static EventMessageOutcome.Timed Timed(GeneralEvent carried, long firesAt)
        => new(new TimedEventMessage(
            carried.EventMessageGroupId,
            carried.EventMessageId,
            carried.EventMessageType,
            carried.TimeMode == GeneralEvent.Immediate ? EventTimeMode.Immediate : EventTimeMode.Npt,
            firesAt,
            carried.PrivateData));

    private static long OnSystemClock(NptReference reference, long npt)
        => Wrapped(reference.Stc + ((npt - reference.Npt) * reference.ScaleDenominator / reference.ScaleNumerator));

    private static long Wrapped(long pts) => ((pts % PtsModulus) + PtsModulus) % PtsModulus;

    private readonly record struct SectionKey(int TableIdExtension, int SectionNumber);
}
