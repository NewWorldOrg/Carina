using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed class RuleTakingDomainService(IReservationRepository reservations)
{
    public async Task<RuleProgrammeTaking> TakingAsync(
        ProgrammeRef programme,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        if (await reservations.FindByProgrammeAsync(programme, cancellationToken) is not { } already)
        {
            return new RuleProgrammeTaking(RuleTakingMove.Make, null);
        }

        return new RuleProgrammeTaking(
            already.MayBeRevivedByRule(at) ? RuleTakingMove.Revive : RuleTakingMove.Leave,
            already);
    }

    public async Task<IReadOnlyList<Reservation>> ChangedByHandAsync(
        IReadOnlyList<Rule> standing,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(standing);

        Dictionary<RuleId, Rule> asked = [];

        foreach (Rule rule in standing)
        {
            asked.TryAdd(rule.Id, rule);
        }

        IReadOnlyList<Reservation> pending = await reservations.ListPendingAsync(
            new ReservationWindow(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            cancellationToken);

        return
        [
            .. pending.Where(reservation => reservation.RuleId is { } ruleId
                                            && asked.TryGetValue(ruleId, out Rule? rule)
                                            && !reservation.IsAsTheRuleAsks(rule)),
        ];
    }
}
