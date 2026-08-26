using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public sealed class Recording
{
    private List<Interruption> interruptions = [];

    private List<OutcomeDetail> outcomeDetail = [];

    private Recording()
    {
    }

    public RecordingId Id { get; private set; } = null!;

    public ReservationId? ReservationId { get; private set; }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public DateTime ProgrammeStartsAt { get; private set; }

    public OutputRoot OutputRoot { get; private set; } = null!;

    public RecordingFileName FileName { get; private set; } = null!;

    public long? FileSizeObserved { get; private set; }

    public DateTime? ObservedAt { get; private set; }

    public DateTime StartedAtActual { get; private set; }

    public DateTime? StoppedAtActual { get; private set; }

    public DateTime? AbortedAt { get; private set; }

    public long WrittenDurationMs { get; private set; }

    public int ResumeCount { get; private set; }

    public IReadOnlyList<Interruption> Interruptions
    {
        get => interruptions;
        private set => interruptions = [.. value];
    }

    public DateTime ExpectedWindowStart { get; private set; }

    public DateTime ExpectedWindowEnd { get; private set; }

    public RecordingOutcome? Outcome { get; private set; }

    public IReadOnlyList<OutcomeDetail> OutcomeDetail
    {
        get => outcomeDetail;
        private set => outcomeDetail = [.. value];
    }

    public bool CcMeasured { get; private set; }

    public long? CcDroppedPackets { get; private set; }

    public long? CcTotalPackets { get; private set; }

    public DropCounters Counters => DropCounters.Rehydrate(CcMeasured, CcDroppedPackets, CcTotalPackets);

    public DropTimeline Positions { get; private set; } = DropTimeline.Unlocated;

    public long? ScrambledPackets { get; private set; }

    public long EovfCount { get; private set; }

    public TunerDeviceId? TunerDeviceId { get; private set; }

    public ThumbnailState ThumbnailState { get; private set; } = ThumbnailState.Pending;

    public ThumbnailFault? ThumbnailFault { get; private set; }

    public DateTime? MeasuredUpdatedAt { get; private set; }

    public string SnapshotName { get; private set; } = string.Empty;

    public string SnapshotSummary { get; private set; } = string.Empty;

    public string SnapshotExtended { get; private set; } = string.Empty;

    public IReadOnlyList<ProgrammeGenre> SnapshotGenres { get; private set; } = [];

    public DateTime CapturedAt { get; private set; }

    public BroadcastGroupKey? BroadcastGroupKey { get; private set; }

    public BroadcastGroupRole BroadcastGroupRole { get; private set; }

    public ProgrammeRef Programme => new(NetworkId, ServiceId, EventId, ProgrammeStartsAt);

    public bool IsInFlight => Outcome is null;

    public bool ThumbnailShowsAnUnfinishedRecording
        => ThumbnailState is ThumbnailState.Ready && Outcome is RecordingOutcome.Truncated;

    public TimeSpan Written => TimeSpan.FromMilliseconds(WrittenDurationMs);

    public static Recording Begin(
        RecordingId id,
        ReservationId? reservationId,
        ProgrammeRef programme,
        OutputRoot outputRoot,
        RecordingFileName fileName,
        DateTime expectedWindowStart,
        DateTime expectedWindowEnd,
        ProgrammeSnapshot snapshot,
        BroadcastGroupKey? broadcastGroupKey,
        BroadcastGroupRole broadcastGroupRole,
        DateTime at,
        TunerDeviceId? tunerDeviceId = null)
        => Rehydrate(
            id,
            reservationId,
            programme,
            outputRoot,
            fileName,
            null,
            null,
            at,
            null,
            null,
            0,
            0,
            [],
            expectedWindowStart,
            expectedWindowEnd,
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            tunerDeviceId,
            ThumbnailState.Pending,
            snapshot,
            broadcastGroupKey,
            broadcastGroupRole);

    public static Recording Rehydrate(
        RecordingId id,
        ReservationId? reservationId,
        ProgrammeRef programme,
        OutputRoot outputRoot,
        RecordingFileName fileName,
        long? fileSizeObserved,
        DateTime? observedAt,
        DateTime startedAtActual,
        DateTime? stoppedAtActual,
        DateTime? abortedAt,
        long writtenDurationMs,
        int resumeCount,
        IReadOnlyList<Interruption> interruptions,
        DateTime expectedWindowStart,
        DateTime expectedWindowEnd,
        RecordingOutcome? outcome,
        IReadOnlyList<OutcomeDetail> outcomeDetail,
        DropCounters counters,
        DropTimeline positions,
        long? scrambledPackets,
        long eovfCount,
        DateTime? measuredUpdatedAt,
        TunerDeviceId? tunerDeviceId,
        ThumbnailState thumbnailState,
        ProgrammeSnapshot snapshot,
        BroadcastGroupKey? broadcastGroupKey,
        BroadcastGroupRole broadcastGroupRole,
        ThumbnailFault? thumbnailFault = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(interruptions);
        ArgumentNullException.ThrowIfNull(outcomeDetail);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!fileName.Names(id))
        {
            throw new ArgumentException(
                "A recording file carries the id of the recording it holds, so the two can always find each other.",
                nameof(fileName));
        }

        if (expectedWindowEnd <= expectedWindowStart)
        {
            throw new ArgumentException("A recording window ends after it starts.", nameof(expectedWindowEnd));
        }

        if (writtenDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writtenDurationMs),
                writtenDurationMs,
                "A recording cannot have written a negative length.");
        }

        if (resumeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resumeCount), resumeCount, "A resume count is not negative.");
        }

        if (fileSizeObserved is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeObserved),
                fileSizeObserved,
                "A file is not smaller than empty.");
        }

        if (fileSizeObserved is null != observedAt is null)
        {
            throw new ArgumentException(
                "A size that was read off the disk says when it was read.",
                nameof(observedAt));
        }

        if (scrambledPackets is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scrambledPackets),
                scrambledPackets,
                "A count of packets left scrambled is not negative.");
        }

        if (eovfCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eovfCount), eovfCount, "An overflow count is not negative.");
        }

        RefuseAPositionNothingCounted(counters, positions, scrambledPackets);
        RefuseAMeasurementFromNoTuner(counters, eovfCount, tunerDeviceId);
        RefuseAReasonFromNoTuner(outcomeDetail, tunerDeviceId);
        RefuseAReasonBeforeTheRecordingBegan(outcomeDetail, startedAtActual);

        RefuseAThumbnailForAFailure(outcome, thumbnailState);
        RefuseAPictureThatDoesNotSayWhyItIsMissing(thumbnailState, thumbnailFault);
        RefuseAHistoryThatDoesNotAddUp(interruptions, resumeCount, startedAtActual);
        RefuseATimeBeforeTheRecordingBegan(startedAtActual, stoppedAtActual, nameof(stoppedAtActual));
        RefuseATimeBeforeTheRecordingBegan(startedAtActual, abortedAt, nameof(abortedAt));
        RefuseATimeBeforeTheRecordingBegan(startedAtActual, observedAt, nameof(observedAt));
        RefuseATimeBeforeTheRecordingBegan(startedAtActual, measuredUpdatedAt, nameof(measuredUpdatedAt));

        if (counters.Measured && measuredUpdatedAt is null)
        {
            throw new ArgumentException("Counted packets say when they were last counted.", nameof(measuredUpdatedAt));
        }

        if (!Enum.IsDefined(broadcastGroupRole))
        {
            throw new ArgumentOutOfRangeException(
                nameof(broadcastGroupRole),
                broadcastGroupRole,
                "A recording names a role it can hold.");
        }

        if (broadcastGroupRole is not Reservations.BroadcastGroupRole.Standalone && broadcastGroupKey is null)
        {
            throw new ArgumentException(
                $"A recording in the {broadcastGroupRole} role names the broadcast it belongs to.",
                nameof(broadcastGroupKey));
        }

        if (outcome is { } settled)
        {
            RefuseAnUnreachableOutcome(settled, abortedAt, fileSizeObserved, stoppedAtActual, outcomeDetail);
        }

        return new Recording
        {
            Id = id,
            ReservationId = reservationId,
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            ProgrammeStartsAt = programme.StartsAt,
            OutputRoot = outputRoot,
            FileName = fileName,
            FileSizeObserved = fileSizeObserved,
            ObservedAt = UtcTimes.Optional(observedAt, nameof(observedAt)),
            StartedAtActual = UtcTimes.Required(startedAtActual, nameof(startedAtActual)),
            StoppedAtActual = UtcTimes.Optional(stoppedAtActual, nameof(stoppedAtActual)),
            AbortedAt = UtcTimes.Optional(abortedAt, nameof(abortedAt)),
            WrittenDurationMs = writtenDurationMs,
            ResumeCount = resumeCount,
            ExpectedWindowStart = UtcTimes.Required(expectedWindowStart, nameof(expectedWindowStart)),
            ExpectedWindowEnd = UtcTimes.Required(expectedWindowEnd, nameof(expectedWindowEnd)),
            Outcome = outcome,
            CcMeasured = counters.Measured,
            CcDroppedPackets = counters.Dropped,
            CcTotalPackets = counters.Total,
            Positions = positions,
            ScrambledPackets = scrambledPackets,
            EovfCount = eovfCount,
            MeasuredUpdatedAt = UtcTimes.Optional(measuredUpdatedAt, nameof(measuredUpdatedAt)),
            TunerDeviceId = tunerDeviceId,
            ThumbnailState = thumbnailState,
            ThumbnailFault = thumbnailFault,
            SnapshotName = snapshot.Name,
            SnapshotSummary = snapshot.Summary,
            SnapshotExtended = snapshot.Extended,
            SnapshotGenres = snapshot.Genres,
            CapturedAt = snapshot.CapturedAt,
            BroadcastGroupKey = broadcastGroupKey,
            BroadcastGroupRole = broadcastGroupRole,
            Interruptions = interruptions,
            OutcomeDetail = outcomeDetail,
        };
    }

    public void Extend(DateTime expectedWindowEnd)
    {
        RefuseUnlessInFlight();

        if (expectedWindowEnd <= ExpectedWindowEnd)
        {
            throw new ArgumentException(
                $"A recording only ever follows a programme later, so expected a time after {ExpectedWindowEnd:O}.",
                nameof(expectedWindowEnd));
        }

        ExpectedWindowEnd = UtcTimes.Required(expectedWindowEnd, nameof(expectedWindowEnd));
    }

    public void Wrote(TimeSpan written)
    {
        RefuseUnlessInFlight();

        if (written < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(written), written, "A recording writes forwards.");
        }

        WrittenDurationMs += (long)written.TotalMilliseconds;
    }

    public void Illustrate(ThumbnailState thumbnailState, ThumbnailFault? thumbnailFault = null)
    {
        RefuseAThumbnailForAFailure(Outcome, thumbnailState);
        RefuseAPictureThatDoesNotSayWhyItIsMissing(thumbnailState, thumbnailFault);

        ThumbnailState = thumbnailState;
        ThumbnailFault = thumbnailFault;
    }

    public void Acquire(TunerDeviceId tunerDeviceId)
    {
        ArgumentNullException.ThrowIfNull(tunerDeviceId);
        RefuseUnlessInFlight();

        TunerDeviceId = tunerDeviceId;
    }

    public void Measure(
        DropCounters counters,
        DropTimeline positions,
        long? scrambledPackets,
        long eovfCount,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(positions);
        RefuseUnlessInFlight();

        if (scrambledPackets is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scrambledPackets),
                scrambledPackets,
                "A count of packets left scrambled is not negative.");
        }

        if (eovfCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eovfCount), eovfCount, "An overflow count is not negative.");
        }

        RefuseAPositionNothingCounted(counters, positions, scrambledPackets);
        RefuseAMeasurementFromNoTuner(counters, eovfCount, TunerDeviceId);
        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, at, nameof(at));

        CcMeasured = counters.Measured;
        CcDroppedPackets = counters.Dropped;
        CcTotalPackets = counters.Total;
        Positions = positions;
        ScrambledPackets = scrambledPackets;
        EovfCount = eovfCount;
        MeasuredUpdatedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Interrupt(RecordingFault fault, DateTime at)
    {
        RefuseUnlessInFlight();
        RecordingFaults.BreaksARecording(fault);

        if (interruptions.Count > 0 && interruptions[^1].ResumedAt is null)
        {
            throw new InvalidOperationException("This recording is already interrupted.");
        }

        RefuseATimeBeforeTheRecordingBegan(LatestMoment(), at, nameof(at));

        interruptions.Add(new Interruption(fault, UtcTimes.Required(at, nameof(at)), null));
    }

    public void Resume(DateTime at)
    {
        RefuseUnlessInFlight();

        if (interruptions.Count is 0 || interruptions[^1].ResumedAt is not null)
        {
            throw new InvalidOperationException("This recording was not interrupted.");
        }

        RefuseATimeBeforeTheRecordingBegan(interruptions[^1].OccurredAt, at, nameof(at));

        interruptions[^1] = new Interruption(
            interruptions[^1].Fault,
            interruptions[^1].OccurredAt,
            UtcTimes.Required(at, nameof(at)));
        ResumeCount++;
    }

    public void Note(OutcomeDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        RefuseUnlessInFlight();
        RefuseAnUnnamedFault(detail.Fault);
        RefuseAReasonFromNoTuner([detail], TunerDeviceId);
        RefuseAReasonBeforeTheRecordingBegan([detail], StartedAtActual);

        outcomeDetail.Add(detail);
    }

    public void Abort(DateTime at)
    {
        RefuseUnlessInFlight();
        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, at, nameof(at));

        AbortedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Settle(RecordingOutcome outcome, long fileSizeObserved, DateTime at)
    {
        RefuseUnlessInFlight();

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A recording ends in one of three ways.");
        }

        if (fileSizeObserved < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeObserved),
                fileSizeObserved,
                "A file is not smaller than empty.");
        }

        DateTime stopped = UtcTimes.Required(at, nameof(at));

        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, stopped, nameof(at));
        RefuseAnUnreachableOutcome(outcome, AbortedAt, fileSizeObserved, stopped, outcomeDetail);
        RefuseAThumbnailForAFailure(outcome, ThumbnailState);

        Outcome = outcome;
        FileSizeObserved = fileSizeObserved;
        ObservedAt = stopped;
        StoppedAtActual = stopped;
    }

    private static void RefuseAnUnreachableOutcome(
        RecordingOutcome outcome,
        DateTime? abortedAt,
        long? fileSizeObserved,
        DateTime? stoppedAtActual,
        IReadOnlyList<OutcomeDetail> detail)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A recording ends in one of three ways.");
        }

        if (stoppedAtActual is null)
        {
            throw new ArgumentException("A recording that has an outcome has stopped.", nameof(stoppedAtActual));
        }

        if (outcome is RecordingOutcome.Complete && abortedAt is null)
        {
            throw new ArgumentException(
                "A recording is complete only when this side asked it to stop, so an end nobody asked for is not one.",
                nameof(outcome));
        }

        if (fileSizeObserved is null)
        {
            throw new ArgumentException(
                "A recording that has an outcome was weighed against the file on disk.",
                nameof(fileSizeObserved));
        }

        if (fileSizeObserved is 0 && outcome is not RecordingOutcome.Failed)
        {
            throw new ArgumentException("An empty file is a failure, whatever else was observed.", nameof(outcome));
        }

        if (outcome is not RecordingOutcome.Complete && detail.Count is 0)
        {
            throw new ArgumentException(
                $"A recording that ended {outcome} says why, in the classes the ledger holds.",
                nameof(detail));
        }
    }

    private static void RefuseAPositionNothingCounted(
        DropCounters counters,
        DropTimeline positions,
        long? scrambledPackets)
    {
        if (!positions.Located)
        {
            return;
        }

        if (!counters.Measured)
        {
            throw new ArgumentException(
                "Nothing counted these packets, so there is nowhere in the stream to put them.",
                nameof(positions));
        }

        if (positions.Continuity > counters.Dropped)
        {
            throw new ArgumentException(
                $"A timeline places {positions.Continuity} lost packets, but only {counters.Dropped} were counted.",
                nameof(positions));
        }

        if (positions.Scrambled > (scrambledPackets ?? 0))
        {
            throw new ArgumentException(
                $"A timeline places {positions.Scrambled} scrambled packets, but only {scrambledPackets ?? 0} were counted.",
                nameof(positions));
        }
    }

    private static void RefuseAThumbnailForAFailure(RecordingOutcome? outcome, ThumbnailState thumbnailState)
    {
        if (!Enum.IsDefined(thumbnailState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(thumbnailState),
                thumbnailState,
                "A thumbnail is in one of the four states the ledger holds.");
        }

        if (outcome is RecordingOutcome.Failed && thumbnailState is ThumbnailState.Ready)
        {
            throw new ArgumentException(
                "A recording that failed has no picture, because a picture of it would say it was recorded.",
                nameof(thumbnailState));
        }
    }

    private static void RefuseAPictureThatDoesNotSayWhyItIsMissing(
        ThumbnailState thumbnailState,
        ThumbnailFault? thumbnailFault)
    {
        if (thumbnailFault is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(
                nameof(thumbnailFault),
                thumbnailFault,
                "A thumbnail fault is one the ledger holds.");
        }

        if (thumbnailState is ThumbnailState.Failed && thumbnailFault is null)
        {
            throw new ArgumentException(
                "A picture that could not be drawn says what stopped it, in the classes the ledger holds.",
                nameof(thumbnailFault));
        }

        if (thumbnailState is not ThumbnailState.Failed && thumbnailFault is not null)
        {
            throw new ArgumentException(
                $"A picture that is {thumbnailState} was not stopped by anything, so it names no fault.",
                nameof(thumbnailFault));
        }
    }

    private DateTime LatestMoment()
        => interruptions.Count is 0
            ? StartedAtActual
            : interruptions[^1].ResumedAt ?? interruptions[^1].OccurredAt;

    private static void RefuseAReasonBeforeTheRecordingBegan(
        IReadOnlyList<OutcomeDetail> outcomeDetail,
        DateTime startedAtActual)
    {
        foreach (OutcomeDetail detail in outcomeDetail)
        {
            RefuseATimeBeforeTheRecordingBegan(startedAtActual, detail.NoticedAt, nameof(outcomeDetail));
        }
    }

    private static void RefuseAReasonFromNoTuner(
        IReadOnlyList<OutcomeDetail> outcomeDetail,
        TunerDeviceId? tunerDeviceId)
    {
        if (tunerDeviceId is not null)
        {
            return;
        }

        foreach (OutcomeDetail detail in outcomeDetail)
        {
            if (RecordingFaults.ThatReachedTheTuner.Contains(detail.Fault))
            {
                throw new ArgumentException(
                    $"A recording that ended in {detail.Fault} had a tuner, so it names which one it had.",
                    nameof(tunerDeviceId));
            }
        }
    }

    private static void RefuseAMeasurementFromNoTuner(
        DropCounters counters,
        long eovfCount,
        TunerDeviceId? tunerDeviceId)
    {
        if (tunerDeviceId is not null || (!counters.Measured && eovfCount is 0))
        {
            return;
        }

        throw new ArgumentException(
            "A count came off a tuner, so the recording names which one it came off.",
            nameof(tunerDeviceId));
    }

    private static void RefuseAHistoryThatDoesNotAddUp(
        IReadOnlyList<Interruption> interruptions,
        int resumeCount,
        DateTime startedAtActual)
    {
        DateTime previous = startedAtActual;
        int closed = 0;

        foreach (Interruption interruption in interruptions)
        {
            if (interruption.OccurredAt < previous)
            {
                throw new ArgumentException(
                    "Interruptions are kept in the order they happened, and none of them overlap.",
                    nameof(interruptions));
            }

            if (interruption.ResumedAt is { } resumedAt)
            {
                closed++;
                previous = resumedAt;
            }
            else
            {
                previous = interruption.OccurredAt;
            }
        }

        if (interruptions.Take(Math.Max(interruptions.Count - 1, 0)).Any(interruption => interruption.IsOpen))
        {
            throw new ArgumentException(
                "Only the last interruption is still open.",
                nameof(interruptions));
        }

        if (closed != resumeCount)
        {
            throw new ArgumentException(
                $"A recording that closed {closed} interruptions resumed {closed} times, not {resumeCount}.",
                nameof(resumeCount));
        }
    }

    private static void RefuseATimeBeforeTheRecordingBegan(DateTime began, DateTime? at, string parameterName)
    {
        if (at is { } moment && moment < began)
        {
            throw new ArgumentException(
                $"A recording runs forwards, so nothing about it happens before {began:O}.",
                parameterName);
        }
    }

    private static void RefuseAnUnnamedFault(RecordingFault fault)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A fault is one the ledger holds.");
        }
    }

    private void RefuseUnlessInFlight()
    {
        if (!IsInFlight)
        {
            throw new InvalidOperationException($"This recording already ended {Outcome}.");
        }
    }
}
