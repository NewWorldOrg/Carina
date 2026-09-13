namespace Carina.Domain.Migration;

public enum MigrationCarryStanding
{
    WouldBeAHardLink = 1,

    WouldCrossAMount = 2,

    TheNewRootDoesNotTakeALink = 3,

    NothingIsThereToCarry = 4,
}
