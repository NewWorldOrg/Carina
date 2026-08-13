using Microsoft.AspNetCore.Authorization;

namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var allowedAnonymously =
            context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        if (allowedAnonymously || context.User.Identity?.IsAuthenticated is true)
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    }
}
