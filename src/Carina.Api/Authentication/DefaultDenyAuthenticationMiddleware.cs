using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAuthenticationSchemeProvider schemes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemes);

        if (await AdmitsAsync(context, schemes))
        {
            await next(context);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static async Task<bool> AdmitsAsync(
        HttpContext context,
        IAuthenticationSchemeProvider schemes)
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

        return !registered.Any();
    }
}
