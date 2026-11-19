using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed record RuleWithdrawalRehearsal(IReadOnlyList<Reservation> Withdrawing, IReadOnlyList<Reservation> Sweeping);
