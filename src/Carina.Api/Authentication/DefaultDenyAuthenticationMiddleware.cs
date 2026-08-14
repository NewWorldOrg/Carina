using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAuthenticationSchemeProvider schemes,
        TrustedProxyNetworks trustedProxies)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(trustedProxies);

        if (await AdmitsAsync(context, schemes, trustedProxies))
        {
            await next(context);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static async Task<bool> AdmitsAsync(
        HttpContext context,
        IAuthenticationSchemeProvider schemes,
        TrustedProxyNetworks trustedProxies)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return true;
        }

        if (context.User.Identity?.IsAuthenticated is true)
        {
            return true;
        }

        var registered = await schemes.GetAllSchemesAsync();

        return !registered.Any()
            && trustedProxies.Admits(context.Connection.RemoteIpAddress);
    }
}
