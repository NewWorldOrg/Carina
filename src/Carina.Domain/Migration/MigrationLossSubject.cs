namespace Carina.Domain.Migration;

public enum MigrationLossSubject
{
    DuplicateAvoidance = 1,

    EnclosedCharacters = 2,

    DayBoundary = 3,
}

public static class MigrationLossSubjects
{
    public static readonly IReadOnlyList<MigrationLossSubject> All = [.. Enum.GetValues<MigrationLossSubject>()];

    public static MigrationLossSubject Named(MigrationLossSubject subject)
        => Enum.IsDefined(subject)
            ? subject
            : throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "What the run carried and left diminished is one of the things the record knows to name.");
}
