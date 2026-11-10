namespace Carina.Api.Services;

public enum NotificationFailure
{
    Refused = 1,

    NoSuchDestination = 2,

    NoSuchRule = 3,

    TooManyRules = 4,

    TooSoon = 5,

    Unreadable = 6,
}
