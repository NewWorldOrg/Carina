using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// Whether the guide of the stream a reservation is carried on has been read whole since it was last thrown away,
/// together with how long before its start a reservation is left alone by a rule that still stands.
/// </summary>
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

    /// <summary>
    /// Whether a rule may take <paramref name="reservation"/> back on <paramref name="occasion"/>: the reservation
    /// allows it, and, unless the rule is being deleted, the guide of its stream has been read whole.
    /// </summary>
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
