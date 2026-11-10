namespace Carina.Api.Services;

public enum NotificationFailure
{
    Refused = 1,

    NoSuchDestination = 2,

    NoSuchRule = 3,

    TooManyRules = 4,

    TooSoon = 5,

    Unreadable = 6,

    NoSuchProgramme = 7,

    NoSuchWatch = 8,

    AlreadyBegun = 9,

    AlreadyAsked = 10,

    NoDestination = 11,

    NotWaiting = 12,
}
