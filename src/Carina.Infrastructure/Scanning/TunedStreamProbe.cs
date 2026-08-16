using System.Buffers;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Scans;

namespace Carina.Infrastructure.Scanning;

public sealed class TunedStreamProbe(IDriverClient driver, ScanSettings settings, TimeProvider clock)
{
    private static readonly IReadOnlyList<string> BusyRefusals =
        ["noDeviceFree", "deviceBusy", "deviceUnavailable"];

    public async Task<StreamProbe> ProbeAsync(
        TuningParameters tuning,
        CancellationToken deadline,
        CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var sessionId = SessionId.Parse($"scan-{Guid.NewGuid():n}");
        var tune = TuneParamsOf(tuning);
        var start = await driver.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = sessionId,
                Purpose = SessionPurpose.Scan,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            },
            abort);

        switch (start.Outcome)
        {
            case DriverCallOutcome.Unreachable:
                return StreamProbe.DriverUnreachable(start.Failure!);

            case DriverCallOutcome.Refused:
                return Refused(start.Problem!);
        }

        if (!start.TryGetValue(out var session))
        {
            return StreamProbe.DriverUnreachable("The driver accepted the scan session but described none.");
        }

        try
        {
            if (session.State is SessionState.Failed)
            {
                return StreamProbe.Attempted(
                    ScanAttemptOutcome.NoLock,
                    session.FailureCause ?? session.FirstFault ?? "The driver could not tune this channel.");
            }

            return await ReadAsync(tuning, session.SessionId, deadline, abort);
        }
        finally
        {
            await driver.StopSessionAsync(session.SessionId, CancellationToken.None);
        }
    }

    private static StreamProbe Refused(DriverProblem problem)
    {
        var detail = $"{problem.Title}: {string.Join(" ", problem.Problems)}";

        return BusyRefusals.Contains(problem.Title, StringComparer.Ordinal)
            ? StreamProbe.TunersBusy(detail)
            : StreamProbe.Attempted(ScanAttemptOutcome.NoLock, detail);
    }

    private async Task<StreamProbe> ReadAsync(
        TuningParameters tuning,
        SessionId sessionId,
        CancellationToken deadline,
        CancellationToken abort)
    {
        var measurement = await MeasureAsync(sessionId, abort);

        if (measurement is { Locked: false })
        {
            return StreamProbe.Attempted(
                ScanAttemptOutcome.NoLock,
                "The frontend never reported a lock on this channel.") with
            {
                Measurement = measurement,
            };
        }

        var opened = await driver.OpenSessionStreamAsync(
            sessionId,
            DriverEndpoints.SurveySubscriber,
            abort);

        if (opened.Outcome is DriverCallOutcome.Unreachable)
        {
            return StreamProbe.DriverUnreachable(opened.Failure!);
        }

        if (!opened.TryGetValue(out var stream))
        {
            return StreamProbe.Attempted(
                ScanAttemptOutcome.LockedWithoutData,
                opened.Problem is { } problem
                    ? $"{problem.Title}: {string.Join(" ", problem.Problems)}"
                    : "The driver opened no transport stream for this session.") with
            {
                Measurement = measurement,
            };
        }

        var harvest = new TableHarvest();

        await using (stream)
        {
            await HarvestAsync(stream, harvest, deadline, abort);
        }

        return Classify(tuning, harvest) with { Measurement = measurement };
    }

    private async Task HarvestAsync(
        Stream stream,
        TableHarvest harvest,
        CancellationToken deadline,
        CancellationToken abort)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(deadline, abort);
        var buffer = ArrayPool<byte>.Shared.Rent(settings.ReadBufferSize);

        try
        {
            while (!harvest.IsComplete)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, settings.ReadBufferSize), reading.Token);

                if (read == 0)
                {
                    return;
                }

                harvest.Push(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static StreamProbe Classify(TuningParameters tuning, TableHarvest harvest)
    {
        if (harvest.Bytes == 0)
        {
            return StreamProbe.Attempted(
                ScanAttemptOutcome.LockedWithoutData,
                "The frontend locked but the demux delivered no bytes.");
        }

        if (harvest.Network is not { } network || harvest.Description is not { } description)
        {
            return StreamProbe.Attempted(ScanAttemptOutcome.IncompleteTables, harvest.Describe());
        }

        var observed = new TransportStreamId(description.TransportStreamId);

        if (tuning.TransportStreamId is { } wanted && !wanted.Equals(observed))
        {
            return StreamProbe.Attempted(
                ScanAttemptOutcome.UnexpectedStream,
                $"This slot was tuned for stream {wanted.Value} but carries stream {observed.Value}.") with
            {
                ObservedTransportStreamId = observed,
                Network = network,
                Description = description,
            };
        }

        if (!network.Carries(description.TransportStreamId))
        {
            return StreamProbe.Attempted(
                ScanAttemptOutcome.UnexpectedStream,
                $"The service description names stream {observed.Value},"
                + $" which network {network.NetworkId} does not list as its own.") with
            {
                ObservedTransportStreamId = observed,
                Network = network,
                Description = description,
            };
        }

        return StreamProbe.Attempted(ScanAttemptOutcome.Succeeded) with
        {
            ObservedTransportStreamId = observed,
            Network = network,
            Description = description,
        };
    }

    private async Task<SignalMeasurement?> MeasureAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var tuners = await driver.GetTunersAsync(cancellationToken);

        if (!tuners.TryGetValue(out var snapshots))
        {
            return null;
        }

        var quality = snapshots
            .FirstOrDefault(tuner => tuner.CurrentSession?.SessionId == sessionId)?
            .SignalQuality;

        if (quality is null)
        {
            return null;
        }

        var measuredAt = (quality.MeasuredAt ?? clock.GetUtcNow()).UtcDateTime;

        if (quality.Lock is not SignalLock.Locked)
        {
            return SignalMeasurement.WithoutLock(measuredAt);
        }

        var layer = quality.PostViterbiBitErrors.OrderBy(counts => counts.Layer).FirstOrDefault();

        return SignalMeasurement.WithLock(
            measuredAt,
            quality.CnrMilliDecibels,
            layer?.ErrorBits,
            layer?.TotalBits);
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
                "There is no tune for a system this build cannot name."),
        };
}
