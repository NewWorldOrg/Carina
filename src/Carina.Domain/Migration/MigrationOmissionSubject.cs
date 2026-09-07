namespace Carina.Domain.Migration;

public enum MigrationOmissionSubject
{
    ProgrammeGuide = 1,

    DuplicateAvoidance = 2,

    QualityTimeSeries = 3,

    RecordingHistory = 4,
}

public static class MigrationOmissionSubjects
{
    public static readonly IReadOnlyList<MigrationOmissionSubject> All =
        [.. Enum.GetValues<MigrationOmissionSubject>()];

    public static MigrationOmissionSubject Named(MigrationOmissionSubject subject)
        => Enum.IsDefined(subject)
            ? subject
            : throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "What was deliberately not done is one of the things the record knows to say.");
}
