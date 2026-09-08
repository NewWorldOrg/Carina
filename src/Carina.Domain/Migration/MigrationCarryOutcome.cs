namespace Carina.Domain.Migration;

public enum MigrationCarryOutcome
{
    Linked = 1,

    SourceGone = 2,

    AlreadyThere = 3,

    NotOnTheSameFilesystem = 4,

    Refused = 5,
}
