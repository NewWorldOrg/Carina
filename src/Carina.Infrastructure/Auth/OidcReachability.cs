using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class OidcReachability : IOidcReachability
{
    private int state = (int)OidcReach.NotConfigured;

    public OidcReach State => (OidcReach)Volatile.Read(ref state);

    public void Record(OidcReach reach) => Volatile.Write(ref state, (int)reach);
}
