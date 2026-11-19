using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public enum RuleTakingMove
{
    Make = 1,

    Revive = 2,

    Leave = 3,
}

public sealed record RuleProgrammeTaking(RuleTakingMove Move, Reservation? Already);
