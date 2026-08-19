namespace Carina.Domain.Auth;

public enum OidcReach
{
    NotConfigured,
    Reachable,
    OutOfReach,
}

public interface IOidcReachability
{
    OidcReach State { get; }

    void Record(OidcReach reach);
}
