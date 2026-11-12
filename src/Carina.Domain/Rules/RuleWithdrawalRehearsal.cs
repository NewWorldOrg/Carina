using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// The reservations a rule not yet saved would take back while it stands, and those it would take back if deleted.
/// </summary>
public sealed record RuleWithdrawalRehearsal(IReadOnlyList<Reservation> Withdrawing, IReadOnlyList<Reservation> Sweeping);
