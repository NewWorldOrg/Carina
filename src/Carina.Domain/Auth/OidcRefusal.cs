namespace Carina.Domain.Auth;

public enum OidcRefusal
{
    None,
    NoIdentityProviderIsConfigured,
    TheIdentityProviderIsOutOfReach,
    NoHandshakeAnsweredToThatState,
    TheHandshakeBelongsToAnotherBrowser,
    TheHandshakeLapsed,
    TheCodeWasRefused,
    TheIdTokenDidNotVerify,
    TheIssuerIsNotTheOneConfigured,
    TheTokenWasIssuedForSomebodyElse,
    TheIdTokenExpired,
    TheNonceIsNotTheOneIssued,
    TheGroupsOverflowedOutOfTheToken,
    OutsideEveryAllowedGroupAndDomain,
}
