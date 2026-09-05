using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed class LogoVisit
{
    private LogoVisit()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public LogoVisitOutcome Outcome { get; private set; }

    public DateTime LastAttemptedAt { get; private set; }

    public DateTime? LastCollectedAt { get; private set; }

    public static LogoVisit Record(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        LogoVisitOutcome outcome,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);

        var visit = new LogoVisit
        {
            NetworkId = networkId,
            TransportStreamId = transportStreamId,
        };

        visit.Record(outcome, at);

        return visit;
    }

    public static LogoVisit Rehydrate(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        LogoVisitOutcome outcome,
        DateTime lastAttemptedAt,
        DateTime? lastCollectedAt)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);

        return new LogoVisit
        {
            NetworkId = networkId,
            TransportStreamId = transportStreamId,
            Outcome = outcome,
            LastAttemptedAt = UtcTimes.Required(lastAttemptedAt, nameof(lastAttemptedAt)),
            LastCollectedAt = UtcTimes.Optional(lastCollectedAt, nameof(lastCollectedAt)),
        };
    }

    public void Record(LogoVisitOutcome outcome, DateTime at)
    {
        Outcome = outcome;
        LastAttemptedAt = UtcTimes.Required(at, nameof(at));

        if (outcome is LogoVisitOutcome.Collected)
        {
            LastCollectedAt = LastAttemptedAt;
        }
    }

    public DateTime? DueAt(LogoSweepSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Outcome switch
        {
            LogoVisitOutcome.Interrupted => LastAttemptedAt,
            LogoVisitOutcome.Collected => (LastCollectedAt ?? LastAttemptedAt) + settings.BetweenVisits,
            _ => LastAttemptedAt + settings.BeforeRetrying,
        };
    }
}
