using Microsoft.AspNetCore.Authorization;

namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
