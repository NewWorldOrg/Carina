using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// Decides what the rules do with the programmes they take, reading the reservations already standing: a programme is
/// reserved once, a reservation the guide took out comes back, and a reservation a person changed is kept as changed.
/// </summary>
public sealed class RuleTakingDomainService(IReservationRepository reservations)
{
    /// <summary>
    /// A programme no reservation stands for is reserved; the reservation standing for it is brought back when a rule
    /// made it and the guide took it out, and is otherwise left as it is.
    /// </summary>
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

    /// <summary>
    /// The reservations still to come that one of <paramref name="standing"/> made and a person has since changed:
    /// their priority, a margin or whether they are encoded is no longer what the rule asks. A rule edited after it made
    /// a reservation makes that reservation look changed too.
    /// </summary>
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
