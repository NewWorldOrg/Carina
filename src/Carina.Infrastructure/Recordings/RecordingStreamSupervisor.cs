using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed record RecordingWatch(
    int Watched,
    int Kept,
    int Broken,
    int Resumed,
    int Settled,
    int Collisions,
    int LeftOpen)
{
    public static readonly RecordingWatch Nothing = new(0, 0, 0, 0, 0, 0, 0);

    public bool SaysAnything => Broken > 0 || Resumed > 0 || Settled > 0 || Collisions > 0 || LeftOpen > 0;
}

public sealed class RecordingStreamSupervisor(
    IServiceScopeFactory scopes,
    IDriverClient driver,
    IDriverStatusReader status,
    IRecordingFileWeigher weigher,
    RecordingWatchSettings settings,
    TimeProvider clock,
    ILogger<RecordingStreamSupervisor> logger)
{
    private enum Standing
    {
        Running = 1,

        Ended = 2,

        Unknowable = 3,
    }

    public async Task<RecordingWatch> WatchAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DriverObservation observation = await status.ReadAsync(cancellationToken);
        IReadOnlyList<Recording> running = await InFlightAsync(cancellationToken);
        var tally = new Tally();

        foreach (Recording recording in running)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await WatchOneAsync(recording, observation.Hello, now, tally, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                logger.LogError(
                    failure,
                    "Watching recording {Recording} failed, which leaves it running and untouched.",
                    recording.Id.Wire);
            }
        }

        return tally.Read(running.Count);
    }

    private async Task<IReadOnlyList<Recording>> InFlightAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRecordingRepository>()
            .ListInFlightAsync(cancellationToken);
    }

    private async Task WatchOneAsync(
        Recording recording,
        DriverHello? hello,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        DriverCall<SessionSnapshot> asked = await driver.GetSessionAsync(
            RecordingSessions.Named(recording.Id),
            cancellationToken);
        (Standing standing, SessionSnapshot? session) = StandingOf(asked);

        if (standing is Standing.Running && session is { } live)
        {
            await KeepUpAsync(recording, live, hello, now, tally, cancellationToken);

            return;
        }

        if (standing is Standing.Unknowable)
        {
            return;
        }

        if (recording.AbortedAt is not null || recording.ExpectedWindowEnd <= now)
        {
            await SettleAsync(recording, now, tally, cancellationToken);

            return;
        }

        await ReopenAsync(recording, session, now, tally, cancellationToken);
    }

    private static (Standing Standing, SessionSnapshot? Session) StandingOf(DriverCall<SessionSnapshot> asked)
    {
        if (asked.Outcome is DriverCallOutcome.Refused)
        {
            return (Standing.Ended, null);
        }

        if (!asked.TryGetValue(out SessionSnapshot? session))
        {
            return (Standing.Unknowable, null);
        }

        if (session.Concluded || session.State is SessionState.Stopped or SessionState.Failed)
        {
            return (Standing.Ended, session);
        }

        return session.State is SessionState.Requested or SessionState.Active or SessionState.Stopping
            ? (Standing.Running, session)
            : (Standing.Unknowable, session);
    }

    private async Task KeepUpAsync(
        Recording recording,
        SessionSnapshot session,
        DriverHello? hello,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (hello is null)
        {
            return;
        }

        RecordingSessionDto reading = RecordingSessionDto.Of(hello, session);
        DropCounters counters = reading.CcMeasured
            ? DropCounters.Counted(reading.CcDropped ?? 0, reading.CcTotal ?? 0)
            : DropCounters.Unmeasured;
        DropTimeline positions = Placed(reading.Positions);
        long? scrambled = reading.ScrambledPackets;
        DateTime opened = session.StartedAt.UtcDateTime;
        bool resumed = false;

        bool kept = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                Adopt(loaded, session.DeviceId);
                Advance(loaded, opened, now);
                loaded.Measure(counters, positions, scrambled, reading.EovfCount, now);
                resumed = CloseAnyOpenBreak(loaded, now);

                return true;
            },
            tally,
            cancellationToken);

        if (!kept)
        {
            return;
        }

        tally.Kept++;

        if (resumed)
        {
            tally.Resumed++;
        }
    }

    private async Task ReopenAsync(
        Recording recording,
        SessionSnapshot? session,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        RecordingFault fault = BrokeItOff(session);

        if (await ApplyAsync(recording.Id, loaded => OpenABreak(loaded, fault, now), tally, cancellationToken))
        {
            tally.Broken++;
        }

        if (await TuneOfAsync(recording, cancellationToken) is not { } tune)
        {
            tally.LeftOpen++;

            return;
        }

        for (int attempt = 1; attempt <= settings.AttemptsAtReopening; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DriverCall<SessionSnapshot> answer = await driver.StartSessionAsync(
                Request(recording, tune),
                cancellationToken);

            if (answer.TryGetValue(out SessionSnapshot? reopened))
            {
                await ResumedAsync(recording, reopened, tally, cancellationToken);

                return;
            }

            if (attempt < settings.AttemptsAtReopening)
            {
                await Task.Delay(settings.BetweenReopenings, clock, cancellationToken);
            }
        }

        tally.LeftOpen++;

        logger.LogWarning(
            "Recording {Recording} has lost its stream and {Attempts} attempts to open it again were refused; "
            + "the recording stays interrupted and is tried again.",
            recording.Id.Wire,
            settings.AttemptsAtReopening);
    }

    private async Task ResumedAsync(
        Recording recording,
        SessionSnapshot reopened,
        Tally tally,
        CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        bool resumed = false;

        bool saved = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                Adopt(loaded, reopened.DeviceId);
                resumed = CloseAnyOpenBreak(loaded, at);

                return true;
            },
            tally,
            cancellationToken);

        if (saved && resumed)
        {
            tally.Resumed++;
        }
    }

    private async Task SettleAsync(
        Recording recording,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (await TuneOfAsync(recording, cancellationToken) is not { } tune)
        {
            return;
        }

        ExpectedBitrate bitrate = ExpectedBitrate.Of(tune.Kind);
        long? weighed = await weigher.WeighAsync(recording.OutputRoot, recording.FileName, cancellationToken);
        RecordingOutcome outcome = RecordingOutcome.Failed;

        bool settled = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                RecordingVerdict verdict = CompletionEvaluator.Judge(
                    new RecordingEvidence(
                        weighed,
                        loaded.Written,
                        loaded.ExpectedWindowStart,
                        loaded.ExpectedWindowEnd,
                        loaded.AbortedAt),
                    bitrate,
                    CompletionTolerance.Default);

                foreach (OutcomeDetail detail in verdict.Detail(now))
                {
                    loaded.Note(detail);
                }

                loaded.Settle(verdict.Outcome, weighed ?? 0, now);
                outcome = verdict.Outcome;

                return true;
            },
            tally,
            cancellationToken);

        if (!settled)
        {
            return;
        }

        tally.Settled++;

        logger.LogInformation(
            "Recording {Recording} ended {Outcome} against a file of {Bytes} byte(s).",
            recording.Id.Wire,
            outcome,
            weighed);
    }

    private async Task<TuneParams?> TuneOfAsync(Recording recording, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        TuningResolution resolution = await scope.ServiceProvider
            .GetRequiredService<IServiceTuningDirectory>()
            .ResolveTuningAsync(recording.NetworkId, recording.ServiceId, cancellationToken);

        if (resolution.Tuning is not { } tuning)
        {
            logger.LogWarning(
                "Recording {Recording} is on a service this catalogue can no longer tune ({Refusal}), so neither "
                + "its stream nor the rate its weight is judged against can be named.",
                recording.Id.Wire,
                resolution.Refusal);

            return null;
        }

        return tuning.Typed();
    }

    private async Task<bool> ApplyAsync(
        RecordingId id,
        Func<Recording, bool> change,
        Tally tally,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IRecordingRepository recordings = scope.ServiceProvider.GetRequiredService<IRecordingRepository>();
            Recording? loaded = await recordings.FindAsync(id, cancellationToken);

            if (loaded is null || !loaded.IsInFlight || !change(loaded))
            {
                return false;
            }

            try
            {
                await recordings.SaveAsync(loaded, cancellationToken);

                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                tally.Collisions++;

                if (attempt >= settings.AttemptsAtACollision)
                {
                    throw;
                }

                logger.LogInformation(
                    "Recording {Recording} was written by something else while this side was writing to it; "
                    + "the row is read again and this change is put on top of what landed.",
                    id.Wire);
            }
        }
    }

    private static RecordingFault BrokeItOff(SessionSnapshot? session)
        => session?.StopReason switch
        {
            SessionStopReason.Preempted => RecordingFault.TunerContended,
            SessionStopReason.DrainCapReached => RecordingFault.DrainGraceExpired,
            _ => RecordingFault.DriverLost,
        };

    private static bool OpenABreak(Recording recording, RecordingFault fault, DateTime at)
    {
        if (recording.Interruptions.Count > 0 && recording.Interruptions[^1].IsOpen)
        {
            return false;
        }

        recording.Interrupt(fault, at);

        return true;
    }

    private static bool CloseAnyOpenBreak(Recording recording, DateTime at)
    {
        if (recording.Interruptions.Count is 0 || !recording.Interruptions[^1].IsOpen)
        {
            return false;
        }

        recording.Resume(at);

        return true;
    }

    private static void Adopt(Recording recording, string deviceId)
    {
        if (recording.TunerDeviceId is null && deviceId is { Length: > 0 })
        {
            recording.Acquire(new TunerDeviceId(deviceId));
        }
    }

    private static void Advance(Recording recording, DateTime opened, DateTime now)
    {
        DateTime counted = recording.MeasuredUpdatedAt ?? recording.StartedAtActual;
        DateTime from = opened > counted ? opened : counted;

        if (now > from)
        {
            recording.Wrote(now - from);
        }
    }

    private static DropTimeline Placed(DropPositionsDto? positions)
        => positions is null
            ? DropTimeline.Unlocated
            : DropTimeline.Rehydrate(
                positions.AnchorPcr,
                [.. positions.Buckets.Select(bucket =>
                    new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled))],
                [.. positions.Reanchors.Select(reanchor =>
                    new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After))]);

    private static StartSessionRequest Request(Recording recording, TuneParams tune)
        => new()
        {
            SessionId = RecordingSessions.Named(recording.Id),
            Purpose = SessionPurpose.Recording,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
            OutputRoot = recording.OutputRoot.Value,
            RecordingId = recording.Id.Wire,
            EndsAt = recording.ExpectedWindowEnd,
        };

    private sealed class Tally
    {
        public int Kept { get; set; }

        public int Broken { get; set; }

        public int Resumed { get; set; }

        public int Settled { get; set; }

        public int Collisions { get; set; }

        public int LeftOpen { get; set; }

        public RecordingWatch Read(int watched)
            => new(watched, Kept, Broken, Resumed, Settled, Collisions, LeftOpen);
    }
}
