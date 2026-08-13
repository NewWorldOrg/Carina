using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Sessions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Ipc;

public static class DriverApi
{
    public static void Map(WebApplication app)
    {
        var configuration = app.Services.GetRequiredService<DriverConfiguration>();
        var manager = app.Services.GetRequiredService<TunerSessionManager>();
        var hello = app.Services.GetRequiredService<DriverHello>();
        var hub = app.Services.GetRequiredService<DriverEventHub>();

        RequestDelegate health = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                hello with { Draining = manager.IsDraining },
                DriverJson.Context.DriverHello
            );

        RequestDelegate tuners = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                SessionViews.Tuners(configuration, manager),
                DriverJson.Context.IReadOnlyListTunerSnapshot
            );

        RequestDelegate sessions = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                SessionViews.All(manager, hello),
                DriverJson.Context.IReadOnlyListSessionSnapshot
            );

        RequestDelegate session = context => ShowSession(context, manager, hello);

        RequestDelegate startSession = context => StartSession(context, manager, hello);

        RequestDelegate stopSession = context => StopSession(context, manager, hello);

        var stopping = app.Lifetime.ApplicationStopping;

        RequestDelegate stream = context =>
            SessionStreamHandler.Invoke(context, manager, driverStopping: stopping);

        RequestDelegate events = context => DriverEventStream.Invoke(context, hub);

        app.MapGet(DriverEndpoints.Health, health);
        app.MapGet(DriverEndpoints.Tuners, tuners);
        app.MapGet(DriverEndpoints.Sessions, sessions);
        app.MapPost(DriverEndpoints.Sessions, startSession);
        app.MapGet($"{DriverEndpoints.Sessions}/{{id}}", session);
        app.MapDelete($"{DriverEndpoints.Sessions}/{{id}}", stopSession);
        app.MapGet($"{DriverEndpoints.Sessions}/{{id}}/stream", stream);
        app.MapGet(DriverEndpoints.Events, events);
    }

    internal static Task Write<T>(
        HttpContext context,
        int status,
        T value,
        JsonTypeInfo<T> typeInfo
    )
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            value,
            typeInfo,
            contentType: null,
            cancellationToken: context.RequestAborted
        );
    }

    internal static Task Problem(HttpContext context, int status, string title, string detail) =>
        Write(
            context,
            status,
            new DriverProblem(title, [detail]),
            DriverJson.Context.DriverProblem
        );

    private static async Task StartSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        StartSessionRequest? request;

        try
        {
            request = await context.Request.ReadFromJsonAsync(
                DriverJson.Context.StartSessionRequest,
                context.RequestAborted
            );
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
            when (error is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "malformedRequest",
                $"The body is not the JSON this driver reads: {error.Message}"
            );

            return;
        }

        if (request is null)
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "malformedRequest",
                "The body was empty; a session carries its own parameters."
            );

            return;
        }

        var start = manager.Begin(request);

        if (!start.TryGetSession(out var session))
        {
            var (status, title) = Outcome(start.Refusal);

            await Problem(context, status, title, start.Detail);

            return;
        }

        context.Response.Headers.Location = DriverEndpoints.Session(session.SessionId);

        await Write(
            context,
            StatusCodes.Status201Created,
            SessionViews.Of(session, hello),
            DriverJson.Context.SessionSnapshot
        );
    }

    private static async Task ShowSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out var sessionId))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        if (!manager.TryGet(sessionId, out var session))
        {
            await Problem(
                context,
                StatusCodes.Status404NotFound,
                "noSuchSession",
                $"This driver holds no session called '{sessionId}'."
            );

            return;
        }

        await Write(
            context,
            StatusCodes.Status200OK,
            SessionViews.Of(session, hello),
            DriverJson.Context.SessionSnapshot
        );
    }

    private static async Task StopSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out var sessionId))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        var outcome = manager.Stop(sessionId);

        if (outcome is SessionStopOutcome.NoSuchSession)
        {
            await Problem(
                context,
                StatusCodes.Status404NotFound,
                "noSuchSession",
                $"This driver holds no session called '{sessionId}'."
            );

            return;
        }

        var status = outcome is SessionStopOutcome.Stopping
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status200OK;

        if (!manager.TryGet(sessionId, out var session))
        {
            context.Response.StatusCode = status;

            return;
        }

        await Write(
            context,
            status,
            SessionViews.Of(session, hello),
            DriverJson.Context.SessionSnapshot
        );
    }

    private static (int Status, string Title) Outcome(SessionRefusal refusal) =>
        refusal switch
        {
            SessionRefusal.Rejected => (StatusCodes.Status400BadRequest, "rejected"),
            SessionRefusal.UnknownDevice => (StatusCodes.Status400BadRequest, "unknownDevice"),
            SessionRefusal.WrongDeviceKind => (
                StatusCodes.Status400BadRequest,
                "wrongDeviceKind"
            ),
            SessionRefusal.NoDeviceOfThatKind => (
                StatusCodes.Status400BadRequest,
                "noDeviceOfThatKind"
            ),
            SessionRefusal.UnknownOutputRoot => (
                StatusCodes.Status400BadRequest,
                "unknownOutputRoot"
            ),
            SessionRefusal.DuplicateSession => (
                StatusCodes.Status409Conflict,
                "duplicateSession"
            ),
            SessionRefusal.DisabledDevice => (StatusCodes.Status409Conflict, "disabledDevice"),
            SessionRefusal.DeviceBusy => (StatusCodes.Status409Conflict, "deviceBusy"),
            SessionRefusal.NoDeviceFree => (StatusCodes.Status409Conflict, "noDeviceFree"),
            SessionRefusal.RecordingAlreadyExists => (
                StatusCodes.Status409Conflict,
                "recordingAlreadyExists"
            ),
            SessionRefusal.Draining => (StatusCodes.Status503ServiceUnavailable, "draining"),
            SessionRefusal.OutputUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                "outputUnavailable"
            ),
            SessionRefusal.DeviceUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                "deviceUnavailable"
            ),
            _ => (StatusCodes.Status503ServiceUnavailable, "refused"),
        };
}
