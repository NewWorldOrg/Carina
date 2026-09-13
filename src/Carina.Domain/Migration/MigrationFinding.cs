using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

public enum MigrationFinding
{
    TheNewRootIsEmpty = 1,

    TheNewRootIsNotEmpty = 2,

    TheNewRootIsNotThere = 3,

    WhereEncodesGoIsSettled = 4,

    NothingSaysWhereEncodesGo = 5,

    MoreThanOneSaysWhereEncodesGo = 6,

    TheProfileIsNotOffered = 7,
}

public static class MigrationFindings
{
    public static readonly IReadOnlyList<MigrationFinding> All = [.. Enum.GetValues<MigrationFinding>()];

    public static MigrationFinding Named(MigrationFinding finding)
        => Enum.IsDefined(finding)
            ? finding
            : throw new ArgumentOutOfRangeException(
                nameof(finding),
                finding,
                "What a run found before carrying anything is one of the things the record knows to name.");

    public static IReadOnlyList<MigrationFinding> Under(MigrationStandingSubject subject)
        => MigrationStandingSubjects.Named(subject) switch
        {
            MigrationStandingSubject.TheNewRoot =>
            [
                MigrationFinding.TheNewRootIsEmpty,
                MigrationFinding.TheNewRootIsNotEmpty,
                MigrationFinding.TheNewRootIsNotThere,
            ],
            MigrationStandingSubject.WhereEncodesGo =>
            [
                MigrationFinding.WhereEncodesGoIsSettled,
                MigrationFinding.NothingSaysWhereEncodesGo,
                MigrationFinding.MoreThanOneSaysWhereEncodesGo,
                MigrationFinding.TheProfileIsNotOffered,
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "A subject a run looks at has the findings it can come back with."),
        };

    public static bool WouldStopARunForReal(MigrationFinding finding)
        => Named(finding) is not (MigrationFinding.TheNewRootIsEmpty or MigrationFinding.WhereEncodesGoIsSettled);

    public static MigrationFinding Of(MigrationRootStanding standing)
        => standing switch
        {
            MigrationRootStanding.Empty => MigrationFinding.TheNewRootIsEmpty,
            MigrationRootStanding.NotEmpty => MigrationFinding.TheNewRootIsNotEmpty,
            MigrationRootStanding.Missing => MigrationFinding.TheNewRootIsNotThere,
            _ => throw new ArgumentOutOfRangeException(
                nameof(standing),
                standing,
                "How the new root stands is one of the things the record knows to name."),
        };

    public static MigrationFinding Of(EncodeUnaskedStanding standing)
        => standing switch
        {
            EncodeUnaskedStanding.Settled => MigrationFinding.WhereEncodesGoIsSettled,
            EncodeUnaskedStanding.NothingIsDefined => MigrationFinding.NothingSaysWhereEncodesGo,
            EncodeUnaskedStanding.MoreThanOneIsOffered => MigrationFinding.MoreThanOneSaysWhereEncodesGo,
            EncodeUnaskedStanding.TheProfileIsNotOffered => MigrationFinding.TheProfileIsNotOffered,
            _ => throw new ArgumentOutOfRangeException(
                nameof(standing),
                standing,
                "Where encodes go is settled or unsettled for one of the reasons the record knows to name."),
        };
}
