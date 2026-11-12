using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// The reservations of a rule being deleted: those it takes back, and those it leaves standing without it.
/// </summary>
public sealed record RuleRetiring(IReadOnlyList<Reservation> Withdrawn, IReadOnlyList<Reservation> Kept);
