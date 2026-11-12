namespace Carina.Domain.Notifications;

/// <summary>
/// Why a programme is not asked to be told of.
/// </summary>
public enum ProgrammeWatchRefusal
{
    NoSuchProgramme = 1,

    Shadow = 2,

    AlreadyBegun = 3,

    LeadNotOffered = 4,

    NoDestinationTurnedOn = 5,

    DestinationsNotTurnedOn = 6,

    AlreadyAsked = 7,
}
