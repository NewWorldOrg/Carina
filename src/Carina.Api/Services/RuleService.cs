using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Events;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

namespace Carina.Api.Services;

public enum RuleFailure
{
    NoSuchRule = 1,

    NotWrittenAsARule = 2,

    TunersCannotBeCounted = 3,

    OneIsAlreadyRunning = 4,

    TheRulesCouldNotBeRead = 5,
}

public sealed record RuleDraft(
    string Name,
    RuleQuery Query,
    Priority Priority,
    bool Enabled,
    Margin MarginBefore,
    Margin MarginAfter);

public sealed record RuleSwitched(Rule Rule, IReadOnlyList<Reservation> Withdrawn);

public sealed class RuleService(
    IRuleRepository rules,
    RuleApplicationService applying,
    RuleApplyNow applyNow,
    IRecalculationNotice notice,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<Rule>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<Rule>>.Success(await rules.ListAsync(cancellationToken));

    public async Task<ServiceResult<Rule, RuleFailure>> FindAsync(RuleId id, CancellationToken cancellationToken)
        => await rules.FindAsync(id, cancellationToken) is { } rule
            ? ServiceResult<Rule, RuleFailure>.Success(rule)
            : Missing<Rule>(id);

    public async Task<ServiceResult<Rule, RuleFailure>> WriteAsync(
        RuleDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Rule written = Rule.Draft(
            RuleId.New(),
            draft.Name,
            draft.Query,
            draft.Priority,
            draft.Enabled,
            draft.MarginBefore,
            draft.MarginAfter,
            clock.GetUtcNow().UtcDateTime);

        await rules.AddAsync(written, cancellationToken);

        events.Signal(AppEventName.Rules);

        if (written.Enabled)
        {
            notice.Nudge(RecalculationTrigger.RulesChanged);
        }

        return ServiceResult<Rule, RuleFailure>.Success(written);
    }

    public async Task<ServiceResult<Rule, RuleFailure>> RewriteAsync(
        RuleId id,
        RuleDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await rules.FindAsync(id, cancellationToken) is not { } rule)
        {
            return Missing<Rule>(id);
        }

        rule.Rewrite(draft.Name, draft.Query, draft.Priority, draft.MarginBefore, draft.MarginAfter);

        await rules.SaveAsync(rule, cancellationToken);

        events.Signal(AppEventName.Rules);
        notice.Nudge(RecalculationTrigger.RulesChanged);

        return ServiceResult<Rule, RuleFailure>.Success(rule);
    }

    public async Task<ServiceResult<RuleSwitched, RuleFailure>> SwitchAsync(
        RuleId id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(id, cancellationToken) is not { } rule)
        {
            return Missing<RuleSwitched>(id);
        }

        if (enabled)
        {
            rule.Enable();
        }
        else
        {
            rule.Disable();
        }

        await rules.SaveAsync(rule, cancellationToken);

        IReadOnlyList<Reservation> withdrawn = enabled
            ? []
            : await applying.DroppedAsync(id, cancellationToken);

        events.Signal(AppEventName.Rules);
        notice.Nudge(RecalculationTrigger.RulesChanged);

        return ServiceResult<RuleSwitched, RuleFailure>.Success(new RuleSwitched(rule, withdrawn));
    }

    public async Task<ServiceResult<RuleRetirement, RuleFailure>> RetireAsync(
        RuleId id,
        CancellationToken cancellationToken)
    {
        if (await applying.RetiredAsync(id, cancellationToken) is not { } retired)
        {
            return Missing<RuleRetirement>(id);
        }

        events.Signal(AppEventName.Rules);
        notice.Nudge(RecalculationTrigger.RulesChanged);

        return ServiceResult<RuleRetirement, RuleFailure>.Success(retired);
    }

    public async Task<ServiceResult<RuleRehearsal, RuleFailure>> RehearseAsync(
        RuleId? id,
        RuleDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (id is { } named && await rules.FindAsync(named, cancellationToken) is null)
        {
            return Missing<RuleRehearsal>(named);
        }

        Rule rehearsing = Rule.Draft(
            id ?? RuleId.New(),
            draft.Name,
            draft.Query,
            draft.Priority,
            true,
            draft.MarginBefore,
            draft.MarginAfter,
            clock.GetUtcNow().UtcDateTime);

        if (await applying.RehearsedAsync(rehearsing, cancellationToken) is not { } rehearsed)
        {
            return ServiceResult<RuleRehearsal, RuleFailure>.Failure(
                RuleInput.Because(RuleInputFault.QueryNarrowsNothing),
                RuleFailure.NotWrittenAsARule);
        }

        return rehearsed.Settled.Refusal is SchedulingRefusal.CapacityUnknown
            ? ServiceResult<RuleRehearsal, RuleFailure>.Failure(
                "The tuners cannot be counted right now, so nothing was weighed. A preview answers what a draft "
                + "would take and where it would clash, never as something to find out later.",
                RuleFailure.TunersCannotBeCounted)
            : ServiceResult<RuleRehearsal, RuleFailure>.Success(rehearsed);
    }

    public async Task<ServiceResult<RuleApplyOutcome, RuleFailure>> ApplyNowAsync(
        RuleId id,
        CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(id, cancellationToken) is null)
        {
            return Missing<RuleApplyOutcome>(id);
        }

        RuleApplyOutcome outcome = await applyNow.StartAsync(cancellationToken);

        return outcome.Run is { Pass.Applied: null }
            ? ServiceResult<RuleApplyOutcome, RuleFailure>.Failure(
                "The pass walked, but reading the rules against the guide failed part way through it, so what it "
                + "made and what it took away is not known. A count of none would be read as none having changed, "
                + "which is a different thing from not knowing.",
                RuleFailure.TheRulesCouldNotBeRead)
            : ServiceResult<RuleApplyOutcome, RuleFailure>.Success(outcome);
    }

    public static string Because(RuleApplyVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Refusal switch
        {
            RuleApplyRefusal.OneIsAlreadyRunning =>
                "The rules are being applied right now; only one application runs at a time. What was asked for "
                + "is already noted, so the application that is walking answers for it.",
            RuleApplyRefusal.ARecalculationIsAlreadyRunning =>
                "A recalculation is already walking, and the rules are read again inside it. What was asked for "
                + "is already noted, so that pass answers for it.",
            RuleApplyRefusal.TooSoonAfterTheLastOne =>
                "The last application finished too recently to ask for another. The moment it may be asked for "
                + "again is answered beside this.",
            _ => "The rules could not be applied.",
        };
    }

    private static ServiceResult<T, RuleFailure> Missing<T>(RuleId id)
        => ServiceResult<T, RuleFailure>.Failure($"There is no rule {id.Value}.", RuleFailure.NoSuchRule);
}
