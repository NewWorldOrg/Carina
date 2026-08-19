using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Authentication;

public sealed class TrustedProxyDiagnosis(
    TrustedProxies trusted,
    ILogger<TrustedProxyDiagnosis> logger) : IHostedService
{
    public const string NothingIsTrusted =
        "Nothing is trusted to set {ProxiesKey} or {NetworksKey}, so a request arriving through a reverse proxy "
        + "is read as plain http however the proxy labelled it: the session cookie loses Secure and the identity "
        + "provider is handed an http:// redirect URI it will refuse. Name the proxy or its network to fix this.";

    public const string SomethingIsTrusted =
        "X-Forwarded-For, X-Forwarded-Proto and X-Forwarded-Host are read from {Trusted}. Anything else setting "
        + "them is ignored, and the first request that is ignored says so.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (trusted.TrustsNothing)
        {
            logger.LogWarning(NothingIsTrusted, TrustedProxies.ProxiesKey, TrustedProxies.NetworksKey);
        }
        else
        {
            logger.LogInformation(SomethingIsTrusted, trusted.ToString());
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
