using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed record RuleRetiring(IReadOnlyList<Reservation> Withdrawn, IReadOnlyList<Reservation> Kept);
