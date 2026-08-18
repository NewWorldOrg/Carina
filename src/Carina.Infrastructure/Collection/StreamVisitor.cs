using System.Buffers;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record VisitResult(
    VisitOutcome Outcome,
    ProgrammesWritten Written,
    string? Detail);

public sealed class StreamVisitor(
    IDriverClient driver,
    ProgrammeWriter writer,
    CollectionSettings settings,
    TimeProvider clock)
{
    public async Task<VisitResult> VisitAsync(
        TuningParameters tuning,
        bool hurried,
        CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var sessionId = SessionId.Parse($"epg-{Guid.NewGuid():n}");
        var tune = TuneParamsOf(tuning);
        var start = await driver.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = sessionId,
                Purpose = hurried ? SessionPurpose.SurveyNow : SessionPurpose.Survey,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            },
            abort);

        if (!start.TryGetValue(out var session))
        {
            return new VisitResult(
                VisitOutcome.NoLock,
                new ProgrammesWritten(0, 0, 0),
                start.Failure ?? start.Problem?.Title ?? "The driver described no session.");
        }

        try
        {
            if (session.State is SessionState.Failed)
            {
                return new VisitResult(
                    VisitOutcome.NoLock,
                    new ProgrammesWritten(0, 0, 0),
                    session.FailureCause ?? session.FirstFault ?? "The driver could not tune this channel.");
            }

            return await ReadAsync(session.SessionId, abort);
        }
        finally
        {
            await driver.StopSessionAsync(session.SessionId, "the visit is over", CancellationToken.None);
        }
    }

    private async Task<VisitResult> ReadAsync(SessionId sessionId, CancellationToken abort)
    {
        var opened = await driver.OpenSessionStreamAsync(sessionId, DriverEndpoints.SurveySubscriber, abort);

        if (!opened.TryGetValue(out var stream))
        {
            return new VisitResult(
                VisitOutcome.NoBytes,
                new ProgrammesWritten(0, 0, 0),
                opened.Failure ?? opened.Problem?.Title ?? "The driver opened no stream.");
        }

        var harvest = new StreamHarvest();
        var anyBytes = false;
        var interrupted = false;
        var began = clock.GetTimestamp();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 188);

        try
        {
            await using (stream)
            {
                while (!harvest.CanLetGo)
                {
                    if (clock.GetElapsedTime(began) > settings.LongestVisit)
                    {
                        break;
                    }

                    var got = await stream.ReadAsync(buffer, abort);

                    if (got == 0)
                    {
                        interrupted = harvest.Progress.Services.Count > 0 || anyBytes;

                        break;
                    }

                    anyBytes = true;
                    harvest.Push(buffer.AsSpan(0, got));
                }
            }
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            interrupted = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (!anyBytes && !interrupted && await LockWasLostAsync(sessionId, abort))
        {
            return new VisitResult(VisitOutcome.NoLock, new ProgrammesWritten(0, 0, 0), null);
        }

        var done = harvest.Conclude(interrupted, anyBytes);
        var written = done.Tables.Count > 0
            ? await writer.WriteAsync(done.Tables, CancellationToken.None)
            : new ProgrammesWritten(0, 0, 0);

        return new VisitResult(done.Outcome, written, null);
    }

    private async Task<bool> LockWasLostAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var tuners = await driver.GetTunersAsync(cancellationToken);

        if (!tuners.TryGetValue(out var snapshots))
        {
            return false;
        }

        var quality = snapshots
            .FirstOrDefault(tuner => tuner.CurrentSession?.SessionId == sessionId)?
            .SignalQuality;

        return quality?.Lock is SignalLock.NotLocked;
    }

    private static TuneParams TuneParamsOf(TuningParameters tuning)
        => tuning.System switch
        {
            TuneSystem.IsdbT => TuneParams.Terrestrial(tuning.PhysicalChannel),
            TuneSystem.IsdbSBs => TuneParams.Bs(tuning.PhysicalChannel, tuning.TransportStreamId!.Value),
            TuneSystem.IsdbSCs110 => TuneParams.Cs110(tuning.PhysicalChannel),
            _ => throw new ArgumentOutOfRangeException(
                nameof(tuning),
                tuning.System,
                "A visit tunes terrestrial, BS or CS110."),
        };
}
