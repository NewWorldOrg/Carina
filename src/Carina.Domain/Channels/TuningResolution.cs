namespace Carina.Domain.Channels;

public enum TuningRefusal
{
    None,
    NoSuchService,
    NoSelectedChannel,
    NoTunerForSystem,
    CapacityUnknown,
    LedgerUnreadable,
}

public sealed record TuningResolution
{
    private TuningResolution(
        TuningRefusal refusal,
        CandidateChannelId? candidateChannelId,
        TuningParameters? tuning,
        TuningParameters? channelTuning,
        bool impaired)
    {
        Refusal = refusal;
        CandidateChannelId = candidateChannelId;
        Tuning = tuning;
        ChannelTuning = channelTuning;
        Impaired = impaired;
    }

    public TuningRefusal Refusal { get; }

    public CandidateChannelId? CandidateChannelId { get; }

    public TuningParameters? Tuning { get; }

    /// <summary>
    /// The tuning the selected channel holds, whether or not a tuner can take it now. Null when no
    /// channel was selected for the service.
    /// </summary>
    public TuningParameters? ChannelTuning { get; }

    public bool Impaired { get; }

    public bool CanTune => Refusal is TuningRefusal.None;

    public static TuningResolution Tunable(
        CandidateChannelId candidateChannelId,
        TuningParameters tuning,
        bool impaired)
    {
        ArgumentNullException.ThrowIfNull(candidateChannelId);
        ArgumentNullException.ThrowIfNull(tuning);

        return new TuningResolution(TuningRefusal.None, candidateChannelId, tuning, tuning, impaired);
    }

    public static TuningResolution Refused(TuningRefusal refusal, TuningParameters? channelTuning = null)
    {
        if (refusal is TuningRefusal.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A refusal says why the service cannot be tuned, and there is no such reason for one that can.");
        }

        if (channelTuning is not null && refusal is TuningRefusal.NoSuchService or TuningRefusal.NoSelectedChannel)
        {
            throw new ArgumentException(
                $"A service refused as {refusal} has no selected channel whose tuning could be named.",
                nameof(channelTuning));
        }

        return new TuningResolution(refusal, null, null, channelTuning, impaired: false);
    }
}
