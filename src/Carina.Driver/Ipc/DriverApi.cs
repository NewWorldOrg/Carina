using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Diagnostics;
using Carina.Driver.Events;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Ipc;

public static class DriverApi
{
    public static void Map(WebApplication app)
    {
        DriverConfiguration configuration = app.Services.GetRequiredService<DriverConfiguration>();
        TunerSessionManager manager = app.Services.GetRequiredService<TunerSessionManager>();
        DriverHello hello = app.Services.GetRequiredService<DriverHello>();
        DriverEventHub hub = app.Services.GetRequiredService<DriverEventHub>();
        DiagnosticsStore diagnosticsStore = app.Services.GetRequiredService<DiagnosticsStore>();
        ITunerDetector detector = app.Services.GetRequiredService<ITunerDetector>();

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

        RequestDelegate detected = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                DeviceViews.Detected(detector.Detect()),
                DriverJson.Context.IReadOnlyListDetectedDeviceDto
            );

        TunerLedgerStore ledger = app.Services.GetRequiredService<TunerLedgerStore>();

        RequestDelegate showLedger = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                ledger.View(),
                DriverJson.Context.TunerLedgerDto
            );

        RequestDelegate saveLedger = context => SaveLedger(context, ledger, detector);

        RequestDelegate toggleTuner = context =>
            ToggleTuner(context, configuration, manager);

        RequestDelegate sessions = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                SessionViews.All(manager, hello),
                DriverJson.Context.IReadOnlyListSessionSnapshot
            );

        RequestDelegate session = context => ShowSession(context, manager, hello);

        RequestDelegate startSession = context => StartSession(context, manager, hello);

        RequestDelegate stopSession = context => StopSession(context, manager, hub, hello);

        RequestDelegate extendSession = context => ExtendSession(context, manager, hello);

        DriverLifecycle lifecycle = app.Services.GetRequiredService<DriverLifecycle>();

        RequestDelegate stream = context =>
            SessionStreamHandler.Invoke(
                context,
                manager,
                streamsDetaching: lifecycle.StreamsDetaching
            );

        IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        TimeProvider clock = app.Services.GetRequiredService<TimeProvider>();
        DriverStopRequest stopRequest = app.Services.GetRequiredService<DriverStopRequest>();

        RequestDelegate restart = context =>
            Restart(context, manager, hello, lifetime, clock, stopRequest);

        RequestDelegate events = context => DriverEventStream.Invoke(context, hub);

        RequestDelegate storage = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                StorageViews.Of(configuration),
                DriverJson.Context.IReadOnlyListStorageRootDto
            );

        RequestDelegate diagnostics = context =>
            Write(
                context,
                StatusCodes.Status200OK,
                diagnosticsStore.Snapshot(),
                DriverJson.Context.IReadOnlyListDiagnosticSnapshot
            );

        app.MapGet(DriverEndpoints.Health, health);
        app.MapGet(DriverEndpoints.Diagnostics, diagnostics);
        app.MapGet(DriverEndpoints.Tuners, tuners);
        app.MapPut(DriverEndpoints.Tuners, saveLedger);
        app.MapGet(DriverEndpoints.TunerLedger, showLedger);
        app.MapPatch($"{DriverEndpoints.Tuners}/{{id}}", toggleTuner);
        app.MapGet(DriverEndpoints.DevicesDetected, detected);
        app.MapGet(DriverEndpoints.Sessions, sessions);
        app.MapPost(DriverEndpoints.Sessions, startSession);
        app.MapGet($"{DriverEndpoints.Sessions}/{{id}}", session);
        app.MapPatch($"{DriverEndpoints.Sessions}/{{id}}", extendSession);
        app.MapDelete($"{DriverEndpoints.Sessions}/{{id}}", stopSession);
        app.MapGet($"{DriverEndpoints.Sessions}/{{id}}/stream", stream);
        app.MapGet(DriverEndpoints.Storage, storage);
        app.MapGet(DriverEndpoints.Events, events);
        app.MapPost(DriverEndpoints.Restart, restart);
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

        SessionStart start = manager.Begin(request);

        if (!start.TryGetSession(out TunerSession? session))
        {
            (int status, string? title) = Outcome(start.Refusal);

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

    private static async Task SaveLedger(
        HttpContext context,
        TunerLedgerStore ledger,
        ITunerDetector detector
    )
    {
        IReadOnlyList<TunerConfigEntry>? requested;

        try
        {
            requested = await context.Request.ReadFromJsonAsync(
                DriverJson.Context.IReadOnlyListTunerConfigEntry,
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

        if (requested is null)
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "malformedRequest",
                "The body was empty; a ledger names every tuner it wants kept."
            );

            return;
        }

        LedgerRevision revision = ledger.Save(requested, detector.Detect());

        if (revision.Refusal is not LedgerRefusal.None)
        {
            (int status, string? title) = Outcome(revision.Refusal);

            await Problem(context, status, title, revision.Detail);

            return;
        }

        await Write(
            context,
            StatusCodes.Status200OK,
            ledger.View(),
            DriverJson.Context.TunerLedgerDto
        );
    }

    private static async Task ToggleTuner(
        HttpContext context,
        DriverConfiguration configuration,
        TunerSessionManager manager
    )
    {
        string deviceId = context.Request.RouteValues["id"] as string ?? string.Empty;

        if (new TunerConfigEntry { DeviceId = deviceId }.Validate() is { Count: > 0 } malformed)
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badDeviceId",
                string.Join(" ", malformed)
            );

            return;
        }

        TunerToggleRequest? request;

        try
        {
            request = await context.Request.ReadFromJsonAsync(
                DriverJson.Context.TunerToggleRequest,
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

        IReadOnlyList<string> problems = request?.Validate() ?? ["disabled: the body was empty."];

        if (problems.Count > 0)
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "rejected",
                string.Join(" ", problems)
            );

            return;
        }

        if (!manager.Turn(deviceId, request!.Disabled!.Value))
        {
            await NoSuchTuner(context, deviceId);

            return;
        }

        IReadOnlyList<TunerSnapshot> tuners = SessionViews.Tuners(configuration, manager);

        await Write(
            context,
            StatusCodes.Status200OK,
            tuners.First(tuner => string.Equals(tuner.DeviceId, deviceId, StringComparison.Ordinal)),
            DriverJson.Context.TunerSnapshot
        );
    }

    private static async Task Restart(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello,
        IHostApplicationLifetime lifetime,
        TimeProvider clock,
        DriverStopRequest stopRequest
    )
    {
        if (!manager.TryEnterDrainingUnlessRecording(out IReadOnlyList<TunerSession>? recordings))
        {
            await Problem(
                context,
                StatusCodes.Status409Conflict,
                "recordingInProgress",
                Holding(recordings)
            );

            return;
        }

        try
        {
            await Write(
                context,
                StatusCodes.Status202Accepted,
                new DriverRestartDto
                {
                    InstanceId = hello.InstanceId,
                    AcceptedAt = clock.GetUtcNow(),
                    BudgetSeconds = (int)manager.HardStopBudget.TotalSeconds,
                },
                DriverJson.Context.DriverRestartDto
            );
        }
        finally
        {
            stopRequest.Record();
            lifetime.StopApplication();
        }
    }

    private static string Holding(IReadOnlyList<TunerSession> recordings)
    {
        string names = string.Join(", ", recordings.Select(session => session.SessionId.Value));
        DateTimeOffset last = recordings.Max(session => session.EndsAt);

        return $"{recordings.Count} recording(s) are running ({names}); the driver is not restarted until the last one ends at {last:O}.";
    }

    private static Task NoSuchTuner(HttpContext context, string deviceId) =>
        Problem(
            context,
            StatusCodes.Status404NotFound,
            "noSuchTuner",
            $"This driver holds no tuner called '{deviceId}'."
        );

    private static async Task ShowSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out SessionId sessionId))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        if (!manager.TryGet(sessionId, out TunerSession? session))
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

    private static async Task ExtendSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out SessionId sessionId))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        ExtendSessionRequest? request;

        try
        {
            request = await context.Request.ReadFromJsonAsync(
                DriverJson.Context.ExtendSessionRequest,
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
                "The body was empty; an extension names the time the recording now runs to."
            );

            return;
        }

        SessionExtension extension = manager.Extend(sessionId, request);

        if (!extension.TryGetSession(out TunerSession? session))
        {
            (int status, string title) = Outcome(extension.Outcome);

            await Problem(context, status, title, extension.Detail);

            return;
        }

        await Write(
            context,
            StatusCodes.Status200OK,
            SessionViews.Of(session, hello),
            DriverJson.Context.SessionSnapshot
        );
    }

    private static (int Status, string Title) Outcome(SessionExtendOutcome outcome) =>
        outcome switch
        {
            SessionExtendOutcome.NoSuchSession => (
                StatusCodes.Status404NotFound,
                "noSuchSession"
            ),
            SessionExtendOutcome.AlreadyEnded => (
                StatusCodes.Status409Conflict,
                SessionRefusalTitles.SessionEnded
            ),
            SessionExtendOutcome.NotARecording => (
                StatusCodes.Status400BadRequest,
                SessionRefusalTitles.NotARecording
            ),
            _ => (StatusCodes.Status400BadRequest, SessionRefusalTitles.NotAnExtension),
        };

    private static async Task StopSession(
        HttpContext context,
        TunerSessionManager manager,
        DriverEventHub hub,
        DriverHello hello
    )
    {
        if (!SessionId.TryParse(context.Request.RouteValues["id"] as string, out SessionId sessionId))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "badSessionId",
                $"A session id is 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(context.Request.Query["reason"]))
        {
            await Problem(
                context,
                StatusCodes.Status400BadRequest,
                "reasonRequired",
                "Say why this session is being stopped: DELETE /sessions/{id}?reason=..."
            );

            return;
        }

        SessionStopOutcome outcome = await manager.StopAsync(sessionId, context.Request.Query["reason"].ToString(), context.RequestAborted);

        if (outcome is not SessionStopOutcome.NoSuchSession)
        {
            hub.Signal(DriverEvents.SessionStopRequested);
        }

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

        int status = outcome is SessionStopOutcome.Stopping
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status200OK;

        if (!manager.TryGet(sessionId, out TunerSession? session))
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

    private static (int Status, string Title) Outcome(LedgerRefusal refusal) =>
        refusal switch
        {
            LedgerRefusal.Empty => (StatusCodes.Status400BadRequest, "emptyLedger"),
            LedgerRefusal.Malformed => (StatusCodes.Status400BadRequest, "rejected"),
            LedgerRefusal.UnknownDevice => (
                StatusCodes.Status400BadRequest,
                "unknownDevice"
            ),
            LedgerRefusal.UndeterminedKind => (
                StatusCodes.Status409Conflict,
                "undeterminedKind"
            ),
            _ => (StatusCodes.Status503ServiceUnavailable, "ledgerUnwritable"),
        };

    private static (int Status, string Title) Outcome(SessionRefusal refusal) =>
        refusal switch
        {
            SessionRefusal.Rejected => (StatusCodes.Status400BadRequest, SessionRefusalTitles.Rejected),
            SessionRefusal.UnknownDevice => (StatusCodes.Status400BadRequest, SessionRefusalTitles.UnknownDevice),
            SessionRefusal.WrongDeviceKind => (
                StatusCodes.Status400BadRequest,
                SessionRefusalTitles.WrongDeviceKind
            ),
            SessionRefusal.NoDeviceOfThatKind => (
                StatusCodes.Status400BadRequest,
                SessionRefusalTitles.NoDeviceOfThatKind
            ),
            SessionRefusal.UnknownOutputRoot => (
                StatusCodes.Status400BadRequest,
                SessionRefusalTitles.UnknownOutputRoot
            ),
            SessionRefusal.DuplicateSession => (
                StatusCodes.Status409Conflict,
                SessionRefusalTitles.DuplicateSession
            ),
            SessionRefusal.DisabledDevice => (StatusCodes.Status409Conflict, SessionRefusalTitles.DisabledDevice),
            SessionRefusal.FaultedDevice => (StatusCodes.Status409Conflict, SessionRefusalTitles.FaultedDevice),
            SessionRefusal.DeviceBusy => (StatusCodes.Status409Conflict, SessionRefusalTitles.DeviceBusy),
            SessionRefusal.NoDeviceFree => (StatusCodes.Status409Conflict, SessionRefusalTitles.NoDeviceFree),
            SessionRefusal.RecordingAlreadyExists => (
                StatusCodes.Status409Conflict,
                SessionRefusalTitles.RecordingAlreadyExists
            ),
            SessionRefusal.CapabilityMissing => (
                StatusCodes.Status501NotImplemented,
                SessionRefusalTitles.CapabilityMissing
            ),
            SessionRefusal.Draining => (StatusCodes.Status503ServiceUnavailable, SessionRefusalTitles.Draining),
            SessionRefusal.OutputUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                SessionRefusalTitles.OutputUnavailable
            ),
            SessionRefusal.DeviceUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                SessionRefusalTitles.DeviceUnavailable
            ),
            SessionRefusal.NoLock => (StatusCodes.Status409Conflict, SessionRefusalTitles.NoLock),
            _ => (StatusCodes.Status503ServiceUnavailable, SessionRefusalTitles.Refused),
        };
}
