namespace Carina.Domain.Migration;

/// <summary>
/// What stops a run for real before it carries anything.
/// </summary>
public enum MigrationHindrance
{
    TheNewRootIsNotEmptyOrNotThere = 1,

    NothingSettlesWhereEncodesGo = 2,

    NoLinkCanBeMade = 3,
}
