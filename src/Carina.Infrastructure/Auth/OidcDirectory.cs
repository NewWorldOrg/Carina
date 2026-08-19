using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class OidcDirectoryCache
{
    private readonly Lock gate = new();

    private string? discoveryUrl;

    private OidcEndpoints? endpoints;

    private DateTime fetchedAt;

    public OidcEndpoints? Fresh(string discoveryUrl, DateTime now, TimeSpan lifetime)
    {
        lock (gate)
        {
            return endpoints is not null
                   && string.Equals(this.discoveryUrl, discoveryUrl, StringComparison.Ordinal)
                   && now < fetchedAt + lifetime
                ? endpoints
                : null;
        }
    }

    public void Hold(string discoveryUrl, OidcEndpoints endpoints, DateTime at)
    {
        lock (gate)
        {
            this.discoveryUrl = discoveryUrl;
            this.endpoints = endpoints;
            fetchedAt = at;
        }
    }

    public void Forget()
    {
        lock (gate)
        {
            discoveryUrl = null;
            endpoints = null;
        }
    }
}

public sealed class OidcDirectory(
    IOidcGateway gateway,
    OidcDirectoryCache cache,
    IOidcReachability reachability,
    OidcLoginPolicy policy,
    TimeProvider clock) : IOidcDirectory
{
    public async Task<OidcEndpoints?> ForAsync(OidcSettings settings, CancellationToken cancellationToken)
    {
        if (settings?.IsConfigured is not true)
        {
            Forget();

            return null;
        }

        OidcEndpoints? held = cache.Fresh(
            settings.DiscoveryUrl!,
            clock.GetUtcNow().UtcDateTime,
            policy.DirectoryLifetime);

        return held ?? await ProbeAsync(settings, cancellationToken);
    }

    public async Task<OidcEndpoints?> ProbeAsync(OidcSettings settings, CancellationToken cancellationToken)
    {
        if (settings?.IsConfigured is not true)
        {
            Forget();

            return null;
        }

        OidcEndpoints? reached = await gateway.ReachAsync(settings.DiscoveryUrl!, cancellationToken);

        if (reached is null)
        {
            reachability.Record(OidcReach.OutOfReach);

            return null;
        }

        cache.Hold(settings.DiscoveryUrl!, reached, clock.GetUtcNow().UtcDateTime);
        reachability.Record(OidcReach.Reachable);

        return reached;
    }

    private void Forget()
    {
        cache.Forget();
        reachability.Record(OidcReach.NotConfigured);
    }
}
