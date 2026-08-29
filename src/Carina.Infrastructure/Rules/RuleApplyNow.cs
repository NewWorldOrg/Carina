using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;

namespace Carina.Infrastructure.Rules;

public sealed record RuleApplySettings
{
    public static readonly TimeSpan DefaultBetweenApplications = TimeSpan.FromMinutes(1);

    public TimeSpan BetweenApplications { get; init; } = DefaultBetweenApplications;
}

public sealed record RuleApplyRun(Guid ApplyId, RecalculationPass Pass);

public sealed record RuleApplyOutcome(RuleApplyRun? Run, RuleApplyVerdict? Refusal);

public interface IRecalculationPass
{
    Task<RecalculationPass> RunAsync(CancellationToken cancellationToken);
}

public sealed class RuleApplyNow(
    IRecalculationNotice notice,
    IRecalculationPass passes,
    RuleApplySettings settings,
    TimeProvider clock)
{
    private readonly Lock gate = new();

    private Guid? running;

    private DateTime? lastFinishedAt;

    public RuleApplyVerdict MayStart()
    {
        lock (gate)
        {
            return Verdict();
        }
    }

    public async Task<RuleApplyOutcome> StartAsync(CancellationToken cancellationToken)
    {
        var applyId = Guid.NewGuid();

        lock (gate)
        {
            RuleApplyVerdict asked = Verdict();

            if (!asked.IsAllowed)
            {
                return new RuleApplyOutcome(null, asked);
            }

            running = applyId;
        }

        try
        {
            notice.Nudge(RecalculationTrigger.RulesChanged);

            RecalculationPass ran = await passes.RunAsync(cancellationToken);

            return ran.Refusal is RecalculationRefusal.OneIsAlreadyRunning
                ? new RuleApplyOutcome(
                    null,
                    new RuleApplyVerdict(RuleApplyRefusal.ARecalculationIsAlreadyRunning, null, null))
                : new RuleApplyOutcome(new RuleApplyRun(applyId, ran), null);
        }
        finally
        {
            lock (gate)
            {
                running = null;
                lastFinishedAt = clock.GetUtcNow().UtcDateTime;
            }
        }
    }

    private RuleApplyVerdict Verdict()
        => RuleApplyGuard.Of(
            running,
            lastFinishedAt,
            clock.GetUtcNow().UtcDateTime,
            settings.BetweenApplications);
}
