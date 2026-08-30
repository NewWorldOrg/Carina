using Carina.Api.Services;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;

namespace Carina.Api.Responder.Rules;

public sealed record RuleResponder(
    Guid Id,
    string Name,
    string Query,
    int Priority,
    bool Enabled,
    int MarginBeforeSeconds,
    int MarginAfterSeconds,
    DateTime CreatedAt)
{
    public static RuleResponder Of(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new RuleResponder(
            rule.Id.Value,
            rule.Name,
            rule.Query.Value,
            rule.Priority.Value,
            rule.Enabled,
            rule.MarginBefore.Seconds,
            rule.MarginAfter.Seconds,
            rule.CreatedAt);
    }
}

public sealed record RuleListResponder(IReadOnlyList<RuleResponder> Rules, int Total)
{
    public static RuleListResponder Of(IReadOnlyList<Rule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return new RuleListResponder([.. rules.Select(RuleResponder.Of)], rules.Count);
    }
}

public sealed record RuleSwitchedResponder(RuleResponder Rule, int Withdrawn)
{
    public static RuleSwitchedResponder Of(RuleSwitched switched)
    {
        ArgumentNullException.ThrowIfNull(switched);

        return new RuleSwitchedResponder(RuleResponder.Of(switched.Rule), switched.Withdrawn.Count);
    }
}

public sealed record RuleRetirementResponder(Guid RuleId, int Withdrawn, int Swept)
{
    public static RuleRetirementResponder Of(RuleRetirement retirement)
    {
        ArgumentNullException.ThrowIfNull(retirement);

        return new RuleRetirementResponder(
            retirement.Rule.Id.Value,
            retirement.Withdrawn.Count,
            retirement.Swept.Count);
    }
}

public sealed record RulePreviewTakeResponder(
    string Programme,
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTime StartsAt,
    DateTime? EndsAt,
    string Name,
    bool AlreadyReserved,
    AllocationVerdict? Verdict);

public sealed record RulePreviewResponder(
    IReadOnlyList<RulePreviewTakeResponder> Takes,
    int Matched,
    int Making,
    int AlreadyReserved,
    int Contended,
    int ContendedAltogether,
    int ExcludedAsShadows,
    int SeatsLeftOut)
{
    public static RulePreviewResponder Of(RuleRehearsal rehearsal)
    {
        ArgumentNullException.ThrowIfNull(rehearsal);

        var making = rehearsal.Making.ToDictionary(
            reservation => (
                reservation.NetworkId.Value,
                reservation.ServiceId.Value,
                reservation.EventId.Value,
                reservation.ProgrammeStartsAt));

        return new RulePreviewResponder(
            [.. rehearsal.Taking.Select(take => Took(take, making, rehearsal.Settled))],
            rehearsal.Taking.Count,
            rehearsal.Making.Count,
            rehearsal.Taking.Count - rehearsal.Making.Count,
            rehearsal.Making.Count(reservation => Verdict(reservation, rehearsal.Settled)
                is AllocationVerdict.Contended),
            rehearsal.Settled.Settled ? rehearsal.Settled.Plan.Contended.Count : 0,
            rehearsal.Shadowed,
            rehearsal.Settled.Settled ? rehearsal.Settled.SeatsLeftOut : 0);
    }

    private static RulePreviewTakeResponder Took(
        RuleMatch take,
        IReadOnlyDictionary<(int, int, int, DateTime), Reservation> making,
        SchedulingRun settled)
    {
        (int, int, int, DateTime) naming = (
            take.Programme.NetworkId.Value,
            take.Programme.ServiceId.Value,
            take.Programme.EventId.Value,
            take.Programme.StartsAt);

        return new RulePreviewTakeResponder(
            $"{take.Programme.NetworkId.Value}-{take.Programme.ServiceId.Value}-{take.Programme.EventId.Value}",
            take.Programme.NetworkId.Value,
            take.Programme.ServiceId.Value,
            take.Programme.EventId.Value,
            take.Programme.StartsAt,
            take.Programme.EndsAt,
            take.Programme.Name,
            !making.ContainsKey(naming),
            making.TryGetValue(naming, out Reservation? proposed) ? Verdict(proposed, settled) : null);
    }

    private static AllocationVerdict? Verdict(Reservation proposed, SchedulingRun settled)
        => settled.Settled && settled.Plan.Answers(proposed.Id) ? settled.Plan.For(proposed.Id).Verdict : null;
}

public sealed record RuleImpactResponder(int Making, int Withdrawing, int ChangingHands, int ExcludedAsShadows)
{
    public static RuleImpactResponder Of(RuleRehearsal rehearsal)
    {
        ArgumentNullException.ThrowIfNull(rehearsal);

        return new RuleImpactResponder(
            rehearsal.Making.Count,
            rehearsal.Withdrawing.Count,
            rehearsal.ChangingHands.Count,
            rehearsal.Shadowed);
    }
}

public sealed record RuleApplicationResponder(
    Guid ApplyId,
    long Revision,
    int Read,
    int Made,
    int Refused,
    int Withdrawn,
    int TurnedOff,
    int Faulted)
{
    public static RuleApplicationResponder Of(RuleApplyRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        RuleApplicationRun? applied = run.Pass.Applied;

        return new RuleApplicationResponder(
            run.ApplyId,
            run.Pass.Revision,
            applied?.Read ?? 0,
            applied?.Made.Count ?? 0,
            applied?.Refused.Count ?? 0,
            applied?.Withdrawn.Count ?? 0,
            applied?.TurnedOff.Count ?? 0,
            applied?.Faulted.Count ?? 0);
    }
}

public sealed record RuleApplicationRefusedResponder(
    RuleApplyRefusal Refusal,
    Guid? RunningApplyId,
    DateTimeOffset? NotBefore)
{
    public static RuleApplicationRefusedResponder Of(RuleApplyVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return new RuleApplicationRefusedResponder(
            verdict.Refusal,
            verdict.RunningId,
            verdict.NotBefore is null ? null : new DateTimeOffset(verdict.NotBefore.Value, TimeSpan.Zero));
    }
}
