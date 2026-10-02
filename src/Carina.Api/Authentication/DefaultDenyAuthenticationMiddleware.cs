namespace Carina.Api.Authentication;

public sealed class DefaultDenyAuthenticationMiddleware
{
    private const string ApiSurface = "/api";

    private readonly RequestDelegate next;
    private readonly IReadOnlyList<AnonymousSurface> anonymous;
    private readonly SignInRecord record;

    public DefaultDenyAuthenticationMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        SignInRecord record)
    {
        this.next = next;
        this.record = record;
        anonymous = AnonymousSurfaces.For(environment);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Admits(context))
        {
            await next(context);
            NoteWhatTheWayInLeft(context);

            return;
        }

        Refuse(context);
    }

    private void Refuse(HttpContext context)
    {
        if (!PageRequest.ExpectsAScreen(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            Note(context);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = LoginRedirect.For(
            $"{context.Request.Path}{context.Request.QueryString}");
    }

    private void Note(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(ApiSurface)
            && SessionCookie.CarriedBy(context.Request) is null)
        {
            record.Write(context, SignInMoment.RefusedWithoutASessionCookie);
        }
    }

    private void NoteWhatTheWayInLeft(HttpContext context)
    {
        if (SignInHappening.LeftOn(context) is { } happened)
        {
            record.Write(
                context,
                happened.Moment,
                happened.Method,
                happened.Device,
                happened.Reason,
                happened.Ended);
        }
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
