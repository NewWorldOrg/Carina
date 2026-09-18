using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Collection;

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
    int LeftOpen,
    int StoodDown,
    int OutOfTouch,
    int Advanced)
{
    public static readonly RecordingWatch Nothing = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool SaysAnything
        => Broken > 0 || Resumed > 0 || Settled > 0 || Collisions > 0 || LeftOpen > 0 || StoodDown > 0
           || OutOfTouch > 0;

    /// <summary>
    /// The counts raised only on the pass a recording moves. LeftOpen and OutOfTouch say it is still
    /// where the last pass left it, so they come back every pass and say nothing new.
    /// </summary>
    public bool AnythingMoved => Broken > 0 || Resumed > 0 || Settled > 0;

    /// <summary>
    /// Raised when a recording still being written wrote more, or counted or placed its losses
    /// differently, on this pass. A recording that runs well raises it on nearly every pass, so what
    /// it tells the screens is paced rather than told each time.
    /// </summary>
    public bool CountsMoved => Advanced > 0;
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
    public const string AlreadyEnded = "this recording already ended on the other side";

    private const string NoSuchSession = "noSuchSession";

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
        (Standing standing, SessionSnapshot? session) = StandingOf(asked, recording, logger);

        if (standing is Standing.Unknowable)
        {
            OutOfTouch(recording, now, tally);

            return;
        }

        if (await FreshAsync(recording.Id, cancellationToken) is not { } row)
        {
            return;
        }

        if (standing is Standing.Running && session is { } live)
        {
            if (ItIsOver(row, now))
            {
                await StandDownAsync(row, live, tally, cancellationToken);
            }
            else
            {
                await KeepUpAsync(row, live, hello, now, tally, cancellationToken);
            }

            return;
        }

        if (SessionRefusalReading.FilledTheDisk(session))
        {
            await FailOnAFullDiskAsync(row, now, tally, cancellationToken);

            return;
        }

        if (ItIsOver(row, now))
        {
            if (session is null)
            {
                await MarkWhatWasLeftBehindAsync(row, now, tally, cancellationToken);
            }
            else
            {
                await SettleAsync(row, session, now, tally, cancellationToken);
            }

            return;
        }

        await ReopenAsync(row, session, now, tally, cancellationToken);
    }

    private async Task FailOnAFullDiskAsync(
        Recording recording,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        long? weighed = await weigher.WeighAsync(recording.OutputRoot, recording.FileName, cancellationToken);

        bool failed = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                loaded.Note(new OutcomeDetail(RecordingFault.DiskExhausted, null, string.Empty, now));
                loaded.Settle(RecordingOutcome.Failed, weighed ?? 0, now);

                return true;
            },
            tally,
            cancellationToken);

        if (!failed)
        {
            return;
        }

        tally.Settled++;

        logger.LogWarning(
            "Recording {Recording} stopped because the disk it is written to is full, so it fails here rather than "
            + "being opened again onto a disk with no room; its file of {Bytes} byte(s) stays where it is.",
            recording.Id.Wire,
            weighed);
    }

    private static bool ItIsOver(Recording recording, DateTime now)
        => recording.AbortedAt is not null || recording.ExpectedWindowEnd <= now;

    private async Task<Recording?> FreshAsync(RecordingId id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Recording? read = await scope.ServiceProvider
            .GetRequiredService<IRecordingRepository>()
            .FindAsync(id, cancellationToken);

        return read is { IsInFlight: true } ? read : null;
    }

    private void OutOfTouch(Recording recording, DateTime now, Tally tally)
    {
        tally.OutOfTouch++;

        if (ItIsOver(recording, now))
        {
            logger.LogWarning(
                "Recording {Recording} is over and the driver will not say whether anything is still writing it, "
                + "so it stays in flight for recovery rather than being given an outcome nothing observed.",
                recording.Id.Wire);
        }
    }

    private async Task StandDownAsync(
        Recording recording,
        SessionSnapshot session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (session.State is SessionState.Stopping)
        {
            return;
        }

        tally.StoodDown++;

        logger.LogWarning(
            "Recording {Recording} has already ended on this side and the driver is still writing it, so the "
            + "session is asked to stop rather than being counted into a recording that is over.",
            recording.Id.Wire);

        await driver.StopSessionAsync(RecordingSessions.Named(recording.Id), AlreadyEnded, cancellationToken);
    }

    private static (Standing Standing, SessionSnapshot? Session) StandingOf(
        DriverCall<SessionSnapshot> asked,
        Recording recording,
        ILogger logger)
    {
        if (asked.Outcome is DriverCallOutcome.Refused)
        {
            if (string.Equals(asked.Problem?.Title, NoSuchSession, StringComparison.Ordinal))
            {
                return (Standing.Ended, null);
            }

            logger.LogWarning(
                "The driver refused to say what it holds for recording {Recording} ({Problem}), which says nothing "
                + "about whether the recording is still being written, so nothing is decided from it.",
                recording.Id.Wire,
                asked.Problem?.Title);

            return (Standing.Unknowable, null);
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
        bool advanced = false;

        bool kept = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                if (ItIsOver(loaded, now) || now <= AsFarAsItIsCounted(loaded))
                {
                    return false;
                }

                RecordingResumption.Adopt(loaded, session.DeviceId);
                bool measured = ReadsDifferently(loaded, counters, positions, scrambled, reading.EovfCount);
                bool wrote = Advance(loaded, opened, now);
                loaded.Measure(counters, positions, scrambled, reading.EovfCount, now);
                advanced = wrote || measured;
                resumed = RecordingResumption.CloseAnyOpenBreak(loaded, now);

                return true;
            },
            tally,
            cancellationToken);

        if (!kept)
        {
            return;
        }

        tally.Kept++;

        if (advanced)
        {
            tally.Advanced++;
        }

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
        TuneFailureKind? tuneFailure = SessionRefusalReading.TuneFailureIn(session);
        bool over = false;

        bool broke = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                over = ItIsOver(loaded, now);

                if (over || !RecordingResumption.OpenABreak(loaded, fault, now))
                {
                    return false;
                }

                if (tuneFailure is not null && session is not null)
                {
                    RecordingResumption.Adopt(loaded, session.DeviceId);
                    loaded.Note(new OutcomeDetail(fault, tuneFailure, string.Empty, now));
                }

                return true;
            },
            tally,
            cancellationToken);

        if (over)
        {
            return;
        }

        if (broke)
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
                RecordingResumption.Request(recording, tune),
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
                RecordingResumption.Adopt(loaded, reopened.DeviceId);
                resumed = RecordingResumption.CloseAnyOpenBreak(loaded, at);

                return true;
            },
            tally,
            cancellationToken);

        if (saved && resumed)
        {
            tally.Resumed++;
        }
    }

    /// <summary>
    /// A recording that is over and whose session the driver does not know is one nobody concluded:
    /// there is no session to have ended it, so there is nothing to judge it against and no reading
    /// of the file that could make it complete. It is marked the way recovery marks what it finds,
    /// so that the two sides that may reach this row — this pass and the hook that runs on the
    /// driver's greeting — cannot disagree over which of them got there first.
    /// </summary>
    private async Task MarkWhatWasLeftBehindAsync(
        Recording recording,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        long? weighed = await weigher.WeighAsync(recording.OutputRoot, recording.FileName, cancellationToken);
        RecordingOutcome outcome = OrphanRecovery.WhatIsLeftOf(weighed);

        bool marked = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                if (!ItIsOver(loaded, now))
                {
                    return false;
                }

                foreach (RecordingFault fault in OrphanRecovery.WhyItEndedWhereItDid(false, weighed))
                {
                    loaded.Note(new OutcomeDetail(fault, null, string.Empty, now));
                }

                loaded.Settle(outcome, weighed ?? 0, now);

                return true;
            },
            tally,
            cancellationToken);

        if (!marked)
        {
            return;
        }

        tally.Settled++;

        logger.LogWarning(
            "Recording {Recording} is over and the driver knows no session of its name, so nothing concluded it; "
            + "it ends {Outcome} against a file of {Bytes} byte(s) that stays where it is.",
            recording.Id.Wire,
            outcome,
            weighed);
    }

    private async Task SettleAsync(
        Recording recording,
        SessionSnapshot session,
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
                if (!ItIsOver(loaded, now))
                {
                    return false;
                }

                if (TheEndItWasToldAbout(session, loaded, now) is { } reached)
                {
                    loaded.Abort(reached);
                }

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

    /// <summary>
    /// A session the driver ended because it reached the end it was opened with ended where this
    /// side asked it to: that end was named in the request that opened the session, and the driver
    /// arriving there first is the same ending as the stop this side asks for once the window has
    /// closed. Which of the two got there first is a race, so the reading is taken off the reason
    /// rather than off who moved first.
    ///
    /// The moment written down is the end the driver was holding — the one the request named, or
    /// the later one an extension moved it to — so a recording judged long afterwards is not said
    /// to have been stopped at the moment somebody looked. An end this side already wrote down
    /// stands, a session that names no end of its own is read at the moment of the judgement, and
    /// one that names an end this recording cannot have reached is left as an end nobody asked
    /// for. Every other reason a session ends is one nobody asked for and goes on reading that way.
    /// </summary>
    private static DateTime? TheEndItWasToldAbout(SessionSnapshot session, Recording recording, DateTime now)
    {
        if (session.StopReason is not SessionStopReason.EndTimeReached || recording.AbortedAt is not null)
        {
            return null;
        }

        DateTime told = session.EndsAt?.UtcDateTime ?? now;
        DateTime reached = told > now ? now : told;

        return reached < recording.StartedAtActual ? null : reached;
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
            _ => SessionRefusalReading.TuneFailureIn(session) is null
                ? RecordingFault.DriverLost
                : RecordingFault.TuneFailed,
        };

    private static DateTime AsFarAsItIsCounted(Recording recording)
        => recording.MeasuredUpdatedAt ?? recording.StartedAtActual;

    private static bool Advance(Recording recording, DateTime opened, DateTime now)
    {
        DateTime counted = AsFarAsItIsCounted(recording);
        DateTime from = opened > counted ? opened : counted;

        if (now <= from)
        {
            return false;
        }

        recording.Wrote(now - from);

        return true;
    }

    private static bool ReadsDifferently(
        Recording recording,
        DropCounters counters,
        DropTimeline positions,
        long? scrambled,
        long overflows)
        => !recording.Counters.Equals(counters)
           || recording.ScrambledPackets != scrambled
           || recording.EovfCount != overflows
           || recording.Positions.AnchorPcr != positions.AnchorPcr
           || !recording.Positions.Buckets.SequenceEqual(positions.Buckets)
           || !recording.Positions.Reanchors.SequenceEqual(positions.Reanchors);

    private static DropTimeline Placed(DropPositionsDto? positions)
        => positions is null
            ? DropTimeline.Unlocated
            : DropTimeline.Rehydrate(
                positions.AnchorPcr,
                [.. positions.Buckets.Select(bucket =>
                    new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled))],
                [.. positions.Reanchors.Select(reanchor =>
                    new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After))]);

    private sealed class Tally
    {
        public int Kept { get; set; }

        public int Broken { get; set; }

        public int Resumed { get; set; }

        public int Settled { get; set; }

        public int Collisions { get; set; }

        public int LeftOpen { get; set; }

        public int StoodDown { get; set; }

        public int OutOfTouch { get; set; }

        public int Advanced { get; set; }

        public RecordingWatch Read(int watched)
            => new(watched, Kept, Broken, Resumed, Settled, Collisions, LeftOpen, StoodDown, OutOfTouch, Advanced);
    }
}
