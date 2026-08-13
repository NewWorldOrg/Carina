using Carina.Contracts;
using Carina.Driver.Sessions;

using Microsoft.AspNetCore.Http;

namespace Carina.Driver.Ipc;

public static class SessionStreamHandler
{
    public const string ContentType = "video/mp2t";

    public static async Task Invoke(HttpContext context, TunerSessionManager manager)
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out var sessionId))
        {
            await DriverApi.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        if (!TryReadKind(context, out var kind))
        {
            await DriverApi.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "unknownSubscriber",
                $"'{DriverEndpoints.SubscriberQuery}' is either '{DriverEndpoints.ViewerSubscriber}' or '{DriverEndpoints.SurveySubscriber}'."
            );

            return;
        }

        if (!manager.TryGet(sessionId, out var session))
        {
            await DriverApi.Problem(
                context,
                StatusCodes.Status404NotFound,
                "noSuchSession",
                $"This driver holds no session called '{sessionId}'."
            );

            return;
        }

        if (session.Concluded)
        {
            await DriverApi.Problem(
                context,
                StatusCodes.Status409Conflict,
                "sessionEnded",
                $"The session '{sessionId}' has ended ({session.StopReason}); the driver keeps no stream to replay."
            );

            return;
        }

        if (!session.Broadcaster.TrySubscribe(kind, out var subscription))
        {
            await DriverApi.Problem(
                context,
                StatusCodes.Status429TooManyRequests,
                "tooManySubscribers",
                $"The session '{sessionId}' carries {session.Broadcaster.SubscriberLimit} readers at a time and they are all taken."
            );

            return;
        }

        try
        {
            await Pump(context, session, subscription);
        }
        finally
        {
            session.Broadcaster.Unsubscribe(subscription);
        }
    }

    private static async Task Pump(
        HttpContext context,
        TunerSession session,
        SessionSubscription subscription
    )
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ContentType;

        await context.Response.StartAsync(context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);

        try
        {
            await foreach (
                var chunk in subscription.Reader.ReadAllAsync(context.RequestAborted)
            )
            {
                await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            context.Abort();

            return;
        }

        if (subscription.IsTruncated || session.State is SessionState.Failed)
        {
            context.Abort();

            return;
        }

        await context.Response.Body.FlushAsync(CancellationToken.None);
    }

    private static bool TryReadKind(HttpContext context, out SubscriberKind kind)
    {
        kind = SubscriberKind.Viewer;

        var asked = context.Request.Query[DriverEndpoints.SubscriberQuery].ToString();

        if (asked.Length is 0 || string.Equals(asked, DriverEndpoints.ViewerSubscriber, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(asked, DriverEndpoints.SurveySubscriber, StringComparison.Ordinal))
        {
            kind = SubscriberKind.Survey;

            return true;
        }

        return false;
    }
}
