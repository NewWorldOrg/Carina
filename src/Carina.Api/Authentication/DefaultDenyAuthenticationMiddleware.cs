using Microsoft.AspNetCore.Authentication;

namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware
{
    private readonly RequestDelegate next;
    private readonly IReadOnlyList<AnonymousSurface> anonymous;

    public DefaultDenyAuthenticationMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        this.next = next;
        anonymous = AnonymousSurfaces.For(environment);
    }

    public async Task InvokeAsync(HttpContext context, IAuthenticationSchemeProvider schemes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemes);

        if (await AdmitsAsync(context, schemes))
        {
            await next(context);

            return;
        }

        Refuse(context);
    }

    private static void Refuse(HttpContext context)
    {
        if (!PageRequest.ExpectsAScreen(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = LoginRedirect.For(
            $"{context.Request.Path}{context.Request.QueryString}");
    }

    private async Task<bool> AdmitsAsync(HttpContext context, IAuthenticationSchemeProvider schemes)
    {
        if (anonymous.Admit(context.Request.Method, context.Request.Path.ToString()))
        {
            return true;
        }

        if (context.User.Identity?.IsAuthenticated is true)
        {
            return true;
        }

        IEnumerable<AuthenticationScheme> registered = await schemes.GetAllSchemesAsync();

        return !registered.Any();
    }
}
