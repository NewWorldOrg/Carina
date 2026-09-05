using System.Buffers;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Logos;

public sealed record LogoVisitResult(
    LogoVisitOutcome Outcome,
    IReadOnlyList<HarvestedLogo> Logos,
    IReadOnlyList<HarvestedLogoLink> Links)
{
    public static LogoVisitResult NothingCameOfIt(LogoVisitOutcome outcome) => new(outcome, [], []);

    public bool WorthWaitingOut { get; init; }
}

public sealed class LogoVisitor(IDriverClient driver, LogoSweepSettings settings, TimeProvider clock)
{
    private const int ReadBufferSize = 188 * 348;

    public async Task<LogoVisitResult> VisitAsync(BroadcastStream stream, CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var sessionId = SessionId.Parse($"logo-{Guid.NewGuid():n}");
        TuneParams tune = stream.Tuning.Typed();
        DriverCall<SessionSnapshot> start = await driver.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = sessionId,
                Purpose = SessionPurpose.Logo,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            },
            abort);

        if (!start.TryGetValue(out SessionSnapshot? session))
        {
            return LogoVisitResult.NothingCameOfIt(Refused(start.Problem)) with
            {
                WorthWaitingOut = SessionRefusalReading.IsWorthWaitingOut(start.Problem),
            };
        }

        try
        {
            return session.State is SessionState.Failed
                ? LogoVisitResult.NothingCameOfIt(LogoVisitOutcome.NoLock)
                : await ReadAsync(stream, session.SessionId, abort);
        }
        finally
        {
            await driver.StopSessionAsync(session.SessionId, "the logo sweep moves on", CancellationToken.None);
        }
    }

    private static LogoVisitOutcome Refused(DriverProblem? problem)
        => problem?.Title == SessionRefusalTitles.NoLock ? LogoVisitOutcome.NoLock : LogoVisitOutcome.Interrupted;

    private async Task<LogoVisitResult> ReadAsync(
        BroadcastStream stream,
        SessionId sessionId,
        CancellationToken abort)
    {
        DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(
            sessionId,
            DriverEndpoints.SurveySubscriber,
            abort);

        if (!opened.TryGetValue(out Stream? carrying))
        {
            return LogoVisitResult.NothingCameOfIt(LogoVisitOutcome.Interrupted);
        }

        var harvest = new LogoHarvest();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        bool interrupted = false;
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(abort);
        using ITimer deadline = clock.CreateTimer(
            _ => Stop(reading),
            null,
            settings.LongestVisit,
            Timeout.InfiniteTimeSpan);

        try
        {
            await using (carrying)
            {
                while (!harvest.EverythingOnTheTransportIsAccountedFor(stream.Services))
                {
                    int got = await carrying.ReadAsync(buffer.AsMemory(0, ReadBufferSize), reading.Token);

                    if (got == 0)
                    {
                        break;
                    }

                    harvest.Push(buffer.AsSpan(0, got));
                }
            }
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            interrupted = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            interrupted = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new LogoVisitResult(Concluded(harvest, interrupted), harvest.Logos, harvest.Links);
    }

    private static LogoVisitOutcome Concluded(LogoHarvest harvest, bool interrupted)
    {
        if (harvest.Logos.Count > 0)
        {
            return LogoVisitOutcome.Collected;
        }

        return interrupted ? LogoVisitOutcome.Interrupted : LogoVisitOutcome.NothingArrived;
    }

    private static void Stop(CancellationTokenSource reading)
    {
        try
        {
            reading.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
