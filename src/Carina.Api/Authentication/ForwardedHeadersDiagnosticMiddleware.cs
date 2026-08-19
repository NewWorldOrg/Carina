using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public sealed class ForwardedHeadersDiagnosticMiddleware
{
    public const string TheProxyIsNotTrusted =
        "A request from {Address} carried {Header} and it was ignored, because that address is in neither "
        + "{ProxiesKey} nor {NetworksKey}. The request is being read as plain http, so the session cookie loses "
        + "Secure and the identity provider is handed an http:// redirect URI. Add that address to be rid of this.";

    public const string TheProxyIsTrusted =
        "A request from {Address} carried {Header} and it was taken: this request was read as {Scheme}. "
        + "The forwarded headers are working.";

    private readonly RequestDelegate next;

    private readonly ILogger<ForwardedHeadersDiagnosticMiddleware> logger;

    private readonly string forwarded;

    private readonly string original;

    private int complained;

    private int confirmed;

    public ForwardedHeadersDiagnosticMiddleware(
        RequestDelegate next,
        IOptions<ForwardedHeadersOptions> options,
        ILogger<ForwardedHeadersDiagnosticMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.next = next;
        this.logger = logger;
        forwarded = options.Value.ForwardedProtoHeaderName;
        original = options.Value.OriginalProtoHeaderName;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Announce(context);

        return next(context);
    }

    private void Announce(HttpContext context)
    {
        bool taken = context.Request.Headers.ContainsKey(original);
        bool ignored = !taken && context.Request.Headers.ContainsKey(forwarded);

        if (ignored && Interlocked.Exchange(ref complained, 1) == 0)
        {
            logger.LogWarning(
                TheProxyIsNotTrusted,
                context.Connection.RemoteIpAddress?.ToString() ?? "an unnamed address",
                forwarded,
                TrustedProxies.ProxiesKey,
                TrustedProxies.NetworksKey);
        }

        if (taken && Interlocked.Exchange(ref confirmed, 1) == 0)
        {
            logger.LogInformation(
                TheProxyIsTrusted,
                context.Connection.RemoteIpAddress?.ToString() ?? "an unnamed address",
                forwarded,
                context.Request.Scheme);
        }
    }
}
