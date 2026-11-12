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

/// <summary>
/// An event a table of the guide names in a group. <see cref="NetworkId"/> is null for an event on the network that
/// carried the table.
/// </summary>
public sealed record AnnouncedEvent(int? NetworkId, int ServiceId, int EventId);

public sealed record AnnouncedEventGroup(RelationKind Kind, IReadOnlyList<AnnouncedEvent> Events);
