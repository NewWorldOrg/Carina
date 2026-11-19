namespace Carina.Api.Authentication;

public enum SignInMoment
{
    RefusedWithoutASessionCookie = 1,

    TheSessionCookieNamedNoSession = 2,

    TheSessionHadExpired = 3,

    TheSessionHadBeenRevoked = 4,

    TheSessionWasUsed = 5,

    ALocalSignInOpenedASession = 6,

    ALocalSignInWasRefused = 7,

    ALocalSignInWasHeldOff = 8,

    TheWayToTheProviderWasOpened = 9,

    TheWayToTheProviderCouldNotBeOpened = 10,

    TheWayBackFromTheProviderOpenedASession = 11,

    TheWayBackFromTheProviderWasRefused = 12,

    SignedOut = 13,

    TheSessionRevokedItself = 14,

    TheSessionRevokedAnother = 15,

    AChangedPasswordRevokedTheOthers = 16,
}
