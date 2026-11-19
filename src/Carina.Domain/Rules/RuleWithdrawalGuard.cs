using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed class RuleWithdrawalGuard
{
    private readonly IReadOnlyDictionary<(int NetworkId, int ServiceId), int> carriers;

    private readonly IReadOnlyDictionary<(int NetworkId, int Stream), VisitOutcome> settled;

    private readonly TimeSpan grace;

    internal RuleWithdrawalGuard(
        IReadOnlyDictionary<(int NetworkId, int ServiceId), int> carriers,
        IReadOnlyDictionary<(int NetworkId, int Stream), VisitOutcome> settled,
        TimeSpan grace)
    {
        this.carriers = carriers;
        this.settled = settled;
        this.grace = grace;
    }

    public bool Lets(Reservation reservation, RuleWithdrawal occasion, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return reservation.MayBeWithdrawnByRule(occasion, grace, at)
               && (occasion is RuleWithdrawal.WhenTheRuleIsDeleted || Collected(reservation));
    }

    private bool Collected(Reservation reservation)
        => carriers.TryGetValue((reservation.NetworkId.Value, reservation.ServiceId.Value), out int carrier)
           && settled.TryGetValue((reservation.NetworkId.Value, carrier), out VisitOutcome outcome)
           && outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly;
}
