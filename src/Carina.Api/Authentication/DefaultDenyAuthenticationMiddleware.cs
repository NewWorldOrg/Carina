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

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Admits(context))
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

    private bool Admits(HttpContext context)
    {
        if (anonymous.Admit(context.Request.Method, context.Request.Path.ToString()))
        {
            return true;
        }

        if (context.User.Identity?.IsAuthenticated is true)
        {
            return true;
        }

        return context.GetEndpoint().IsTicketed() && PlaybackTicketCarrier.OfferedBy(context.Request) is not null;
    }
}
