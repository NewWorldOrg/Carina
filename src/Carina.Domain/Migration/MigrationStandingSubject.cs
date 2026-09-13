namespace Carina.Domain.Migration;

public enum MigrationStandingSubject
{
    TheNewRoot = 1,

    WhereEncodesGo = 2,
}

public static class MigrationStandingSubjects
{
    public static readonly IReadOnlyList<MigrationStandingSubject> All =
        [.. Enum.GetValues<MigrationStandingSubject>()];

    public static MigrationStandingSubject Named(MigrationStandingSubject subject)
        => Enum.IsDefined(subject)
            ? subject
            : throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "What a run looked at before carrying anything is one of the things the record knows to name.");
}
