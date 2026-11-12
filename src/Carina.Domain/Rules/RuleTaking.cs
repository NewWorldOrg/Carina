using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// What a rule taking a programme does: makes a reservation for it, brings back the one the guide took out, or leaves
/// the reservation already standing for it as it is.
/// </summary>
public enum RuleTakingMove
{
    Make = 1,

    Revive = 2,

    Leave = 3,
}

/// <summary>
/// The move a rule taking a programme makes, with the reservation already standing for the programme.
/// </summary>
public sealed record RuleProgrammeTaking(RuleTakingMove Move, Reservation? Already);
