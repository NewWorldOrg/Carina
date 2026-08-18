namespace Carina.Domain.Programmes;

public enum ProgrammeSource
{
    PresentFollowing = 1,

    ScheduleBasic = 2,

    ScheduleExtended = 3,
}

public enum RelationKind
{
    Shared = 1,

    Relayed = 2,

    Moved = 3,
}

public sealed record ProgrammeGenre(int Kind, int Sort);

public sealed record ProgrammeItem(string Heading, string Text);

public sealed record RelatedProgramme(int NetworkId, int ServiceId, int EventId, RelationKind Kind);
