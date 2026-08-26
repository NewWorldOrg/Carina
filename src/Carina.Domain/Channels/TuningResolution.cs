namespace Carina.Domain.Channels;

public enum TuningRefusal
{
    None,
    NoSuchService,
    NoSelectedChannel,
    NoTunerForSystem,
    CapacityUnknown,
}

public sealed record TuningResolution
{
    private TuningResolution(
        TuningRefusal refusal,
        CandidateChannelId? candidateChannelId,
        TuningParameters? tuning,
        bool impaired)
    {
        Refusal = refusal;
        CandidateChannelId = candidateChannelId;
        Tuning = tuning;
        Impaired = impaired;
    }

    public TuningRefusal Refusal { get; }

    public CandidateChannelId? CandidateChannelId { get; }

    public TuningParameters? Tuning { get; }

    public bool Impaired { get; }

    public bool CanTune => Refusal is TuningRefusal.None;

    public static TuningResolution Tunable(
        CandidateChannelId candidateChannelId,
        TuningParameters tuning,
        bool impaired)
    {
        ArgumentNullException.ThrowIfNull(candidateChannelId);
        ArgumentNullException.ThrowIfNull(tuning);

        return new TuningResolution(TuningRefusal.None, candidateChannelId, tuning, impaired);
    }

    public static TuningResolution Refused(TuningRefusal refusal)
    {
        if (refusal is TuningRefusal.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A refusal says why the service cannot be tuned, and there is no such reason for one that can.");
        }

        return new TuningResolution(refusal, null, null, impaired: false);
    }
}
