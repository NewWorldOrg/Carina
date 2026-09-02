namespace Carina.Domain.Streaming;

public sealed class LiveJoin
{
    private LiveJoin(ILiveViewing? viewing, LiveRefusal? refusal, TranscodeCeiling? ceiling, string note)
    {
        Viewing = viewing;
        Refusal = refusal;
        Ceiling = ceiling;
        Note = note;
    }

    public ILiveViewing? Viewing { get; }

    public LiveRefusal? Refusal { get; }

    public TranscodeCeiling? Ceiling { get; }

    public string Note { get; }

    public bool Seated => Viewing is not null;

    public static LiveJoin Joined(ILiveViewing viewing)
    {
        ArgumentNullException.ThrowIfNull(viewing);

        return new LiveJoin(viewing, null, null, string.Empty);
    }

    public static LiveJoin Refused(LiveRefusal refusal, string note)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A viewer is refused for one of the reasons named here.");
        }

        if (refusal is LiveRefusal.TooManyAlready)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                $"A full budget says how full it is, so {nameof(Refused)} takes the ceiling.");
        }

        return new LiveJoin(null, refusal, null, TranscoderNote.Of(note));
    }

    public static LiveJoin Refused(TranscodeCeiling ceiling)
    {
        ArgumentNullException.ThrowIfNull(ceiling);

        return new LiveJoin(null, LiveRefusal.TooManyAlready, ceiling, ceiling.Said);
    }
}
