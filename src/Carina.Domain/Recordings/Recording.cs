using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public sealed class Recording
{
    private List<Interruption> interruptions = [];

    private List<RecordingGap> gaps = [];

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

    public IReadOnlyList<RecordingGap> Gaps
    {
        get => gaps;
        private set => gaps = [.. value];
    }

    public long MissedMs { get; private set; }

    public DateTime ExpectedWindowStart { get; private set; }

    public DateTime ExpectedWindowEnd { get; private set; }

    public DateTime PromisedWindowEnd { get; private set; }

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

    public long? CarriedCcDroppedPackets { get; private set; }

    public long? CarriedCcTotalPackets { get; private set; }

    public DropTimeline CarriedPositions { get; private set; } = DropTimeline.Unlocated;

    public long? CarriedScrambledPackets { get; private set; }

    public long CarriedEovfCount { get; private set; }

    /// <summary>
    /// What the sessions before the one being counted had counted, which the counts of this recording stand on.
    /// </summary>
    public CarriedCount Carried
        => new(
            DropCounters.Rehydrate(CarriedCcDroppedPackets is not null, CarriedCcDroppedPackets, CarriedCcTotalPackets),
            CarriedPositions,
            CarriedScrambledPackets,
            CarriedEovfCount);

    /// <summary>
    /// When the session whose counts were last taken opened, to the millisecond, or <see langword="null"/> when
    /// nothing has said which session was counted.
    /// </summary>
    public DateTime? CountedSessionOpenedAt { get; private set; }

    public TunerDeviceId? TunerDeviceId { get; private set; }

    public ThumbnailState ThumbnailState { get; private set; } = ThumbnailState.Pending;

    public ThumbnailFault? ThumbnailFault { get; private set; }

    public CaptionState CaptionState { get; private set; } = CaptionState.Pending;

    /// <summary>
    /// When the captions were last taken from the recording's file, or null while they are waiting to be.
    /// </summary>
    public DateTime? CaptionsMadeAt { get; private set; }

    /// <summary>
    /// How many times the picture on screen changes in the captions kept, or null unless they are ready.
    /// </summary>
    public int? CaptionPictures { get; private set; }

    /// <summary>
    /// How many times in a row taking the captions has failed.
    /// </summary>
    public int CaptionAttempts { get; private set; }

    public DataBroadcastState DataBroadcastState { get; private set; } = DataBroadcastState.None;

    /// <summary>
    /// When the data broadcast was last taken from the recording's file, made, found missing or failed, or null
    /// while its record is not yet due or is coming.
    /// </summary>
    public DateTime? DataBroadcastMadeAt { get; private set; }

    /// <summary>
    /// How many modules the record of the data broadcast holds, or null unless it is made.
    /// </summary>
    public int? DataBroadcastModules { get; private set; }

    /// <summary>
    /// How many times in all taking the data broadcast has failed.
    /// </summary>
    public int DataBroadcastAttempts { get; private set; }

    public DataBroadcastProgress DataBroadcast => new(DataBroadcastState, DataBroadcastAttempts, DataBroadcastModules);

    public DateTime? MeasuredUpdatedAt { get; private set; }

    public string SnapshotName { get; private set; } = string.Empty;

    public string SnapshotSummary { get; private set; } = string.Empty;

    public string SnapshotExtended { get; private set; } = string.Empty;

    public IReadOnlyList<ProgrammeGenre> SnapshotGenres { get; private set; } = [];

    public AudioMode SnapshotAudio { get; private set; }

    public int SnapshotSounds { get; private set; }

    public DateTime CapturedAt { get; private set; }

    public BroadcastGroupKey? BroadcastGroupKey { get; private set; }

    public BroadcastGroupRole BroadcastGroupRole { get; private set; }

    public DateTime? LeftBehindAt { get; private set; }

    public int? FilesLeftBehind { get; private set; }

    /// <summary>
    /// When the file of a recording that ended with <see cref="RecordingFault.ScramblingUnresolved"/>
    /// was descrambled, or null while it has not been.
    /// </summary>
    public DateTime? DescrambledAt { get; private set; }

    /// <summary>
    /// Whether this recording is encoded once it ends, copied from the reservation it was started for
    /// at the moment it began. Defaults to true.
    /// </summary>
    public bool EncodeWhenRecorded { get; private set; }

    public ProgrammeRef Programme => new(NetworkId, ServiceId, EventId, ProgrammeStartsAt);

    public bool IsInFlight => Outcome is null;

    /// <summary>
    /// Whether the recording ended with <see cref="RecordingFault.ScramblingUnresolved"/> in its outcome
    /// detail and has not been descrambled since.
    /// </summary>
    public bool LeftScrambled => !IsInFlight && EndedScrambled(outcomeDetail) && DescrambledAt is null;

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
        TunerDeviceId? tunerDeviceId = null,
        bool encodeWhenRecorded = true)
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
            broadcastGroupRole,
            encodeWhenRecorded: encodeWhenRecorded);

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
        DateTime promisedWindowEnd,
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
        ThumbnailFault? thumbnailFault = null,
        DateTime? leftBehindAt = null,
        int? filesLeftBehind = null,
        bool encodeWhenRecorded = true,
        DateTime? descrambledAt = null,
        IReadOnlyList<RecordingGap>? gaps = null,
        CarriedCount? carried = null,
        DateTime? countedSessionOpenedAt = null,
        CaptionState captionState = CaptionState.Pending,
        DateTime? captionsMadeAt = null,
        int? captionPictures = null,
        int captionAttempts = 0,
        DataBroadcastProgress? dataBroadcast = null,
        DateTime? dataBroadcastMadeAt = null)
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

        if (promisedWindowEnd <= expectedWindowStart)
        {
            throw new ArgumentException(
                "The end a recording was promised comes after its window starts.",
                nameof(promisedWindowEnd));
        }

        if (promisedWindowEnd > expectedWindowEnd)
        {
            throw new ArgumentException(
                "Following a programme only ever moves a recording's end later, so the end it was promised is not after the one it has.",
                nameof(promisedWindowEnd));
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
        RefuseACarryBeyondTheCount(carried ?? CarriedCount.Nothing, counters, scrambledPackets, eovfCount);
        RefuseAMeasurementFromNoTuner(counters, eovfCount, tunerDeviceId);
        RefuseAReasonFromNoTuner(outcomeDetail, tunerDeviceId);
        RefuseAReasonBeforeTheRecordingBegan(outcomeDetail, startedAtActual);

        RefuseAThumbnailForAFailure(outcome, thumbnailState);
        RefuseAPictureThatDoesNotSayWhyItIsMissing(thumbnailState, thumbnailFault);
        RefuseAHistoryThatDoesNotAddUp(interruptions, resumeCount, startedAtActual);
        RefuseGapsThatDoNotFollowOneAnother(gaps ?? [], startedAtActual);
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

        RefuseALeftoverThatDoesNotAddUp(outcome, leftBehindAt, filesLeftBehind);
        RefuseATimeBeforeTheRecordingBegan(startedAtActual, leftBehindAt, nameof(leftBehindAt));
        RefuseADescramblingThatDoesNotAddUp(outcome, outcomeDetail, stoppedAtActual, descrambledAt);
        RefuseCaptionsThatDoNotAddUp(captionState, captionPictures);
        RefuseCaptionsKeptThatDoNotAddUp(outcome, captionState, captionsMadeAt, captionAttempts);

        DataBroadcastProgress progress = dataBroadcast ?? DueOnceEnded(outcome);

        RefuseADataBroadcastThatDoesNotAddUp(outcome, progress, dataBroadcastMadeAt);

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
            PromisedWindowEnd = UtcTimes.Required(promisedWindowEnd, nameof(promisedWindowEnd)),
            Outcome = outcome,
            CcMeasured = counters.Measured,
            CcDroppedPackets = counters.Dropped,
            CcTotalPackets = counters.Total,
            Positions = positions,
            ScrambledPackets = scrambledPackets,
            EovfCount = eovfCount,
            CarriedCcDroppedPackets = carried?.Counters.Dropped,
            CarriedCcTotalPackets = carried?.Counters.Total,
            CarriedPositions = carried?.Positions ?? DropTimeline.Unlocated,
            CarriedScrambledPackets = carried?.ScrambledPackets,
            CarriedEovfCount = carried?.Overflows ?? 0,
            CountedSessionOpenedAt = ToTheMillisecond(
                UtcTimes.Optional(countedSessionOpenedAt, nameof(countedSessionOpenedAt))),
            MeasuredUpdatedAt = UtcTimes.Optional(measuredUpdatedAt, nameof(measuredUpdatedAt)),
            TunerDeviceId = tunerDeviceId,
            ThumbnailState = thumbnailState,
            ThumbnailFault = thumbnailFault,
            SnapshotName = snapshot.Name,
            SnapshotSummary = snapshot.Summary,
            SnapshotExtended = snapshot.Extended,
            SnapshotGenres = snapshot.Genres,
            SnapshotAudio = snapshot.Audio,
            SnapshotSounds = snapshot.Sounds,
            CapturedAt = snapshot.CapturedAt,
            BroadcastGroupKey = broadcastGroupKey,
            BroadcastGroupRole = broadcastGroupRole,
            LeftBehindAt = UtcTimes.Optional(leftBehindAt, nameof(leftBehindAt)),
            FilesLeftBehind = filesLeftBehind,
            DescrambledAt = UtcTimes.Optional(descrambledAt, nameof(descrambledAt)),
            CaptionState = captionState,
            CaptionsMadeAt = UtcTimes.Optional(captionsMadeAt, nameof(captionsMadeAt)),
            CaptionPictures = captionPictures,
            CaptionAttempts = captionAttempts,
            DataBroadcastState = progress.State,
            DataBroadcastMadeAt = UtcTimes.Optional(dataBroadcastMadeAt, nameof(dataBroadcastMadeAt)),
            DataBroadcastModules = progress.Modules,
            DataBroadcastAttempts = progress.Attempts,
            EncodeWhenRecorded = encodeWhenRecorded,
            Interruptions = interruptions,
            Gaps = gaps ?? [],
            MissedMs = MissedIn(gaps ?? []),
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

    public void Erased(RecordingErasure erasure, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(erasure);

        if (IsInFlight)
        {
            throw new InvalidOperationException(
                "A recording still being written is never thrown away, so no erasure of it is kept.");
        }

        DateTime attempted = UtcTimes.Required(at, nameof(at));

        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, attempted, nameof(at));

        if (erasure.EverythingIsGone)
        {
            LeftBehindAt = null;
            FilesLeftBehind = null;

            return;
        }

        if (erasure.LeftFilesBehind)
        {
            LeftBehindAt = attempted;
            FilesLeftBehind = erasure.FilesLeft;
        }
    }

    /// <summary>
    /// Lifts <see cref="LeftScrambled"/> from a recording that ended with
    /// <see cref="RecordingFault.ScramblingUnresolved"/>, keeping its outcome and outcome detail as they are.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The recording is still being written, or it is not <see cref="LeftScrambled"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="at"/> is not UTC or is before the recording stopped.</exception>
    public void Descrambled(DateTime at)
    {
        if (!LeftScrambled)
        {
            throw new InvalidOperationException(
                IsInFlight
                    ? "A recording still being written has not ended scrambled."
                    : "Only a recording that ended with its scrambling unresolved, and has not been descrambled since, is descrambled.");
        }

        DateTime descrambled = UtcTimes.Required(at, nameof(at));

        RefuseATimeBeforeTheRecordingBegan(StoppedAtActual!.Value, descrambled, nameof(at));

        DescrambledAt = descrambled;

        if (DataBroadcastState is DataBroadcastState.Made or DataBroadcastState.Missing or DataBroadcastState.Failed)
        {
            Keep(DataBroadcast.Descrambled(), null);
        }
    }

    /// <summary>
    /// Keeps where the captions taken from the recording's file stand: waiting, ready with the number of
    /// changes kept, absent, or failed once more.
    /// </summary>
    /// <exception cref="InvalidOperationException">The recording is still being written.</exception>
    /// <exception cref="ArgumentException">
    /// The number of changes does not fit the state, or <paramref name="at"/> is not UTC or is before the recording began.
    /// </exception>
    public void Caption(CaptionState captionState, int? captionPictures, DateTime at)
    {
        if (IsInFlight)
        {
            throw new InvalidOperationException(
                "Captions are taken from a recording once it has ended, never while it is being written.");
        }

        RefuseCaptionsThatDoNotAddUp(captionState, captionPictures);

        DateTime settled = UtcTimes.Required(at, nameof(at));

        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, settled, nameof(at));

        CaptionState = captionState;
        CaptionPictures = captionPictures;
        CaptionsMadeAt = captionState is CaptionState.Pending ? null : settled;
        CaptionAttempts = captionState is CaptionState.Failed ? CaptionAttempts + 1 : 0;
    }

    /// <summary>
    /// Keeps that the data broadcast coming from the recording's file was taken: made with the modules its record
    /// holds, or missing when it holds none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The record is not coming.</exception>
    /// <exception cref="ArgumentException"><paramref name="at"/> is not UTC or is before the recording began.</exception>
    public void DataBroadcastTaken(int modules, DateTime at) => Keep(DataBroadcast.Taken(modules), Settled(at));

    /// <summary>
    /// Keeps that taking the data broadcast coming from the recording's file failed once more.
    /// </summary>
    /// <exception cref="InvalidOperationException">The record is not coming.</exception>
    /// <exception cref="ArgumentException"><paramref name="at"/> is not UTC or is before the recording began.</exception>
    public void DataBroadcastFailed(DateTime at) => Keep(DataBroadcast.Failed(), Settled(at));

    /// <summary>
    /// Puts the record of the data broadcast to coming: one not yet due on a recording that has ended, one that
    /// failed with tries left, or one that is made and no longer kept.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The record is none of those, or is not yet due on a recording still being written.
    /// </exception>
    public void DataBroadcastAgain() => Keep(DueAgain(), null);

    public void Acquire(TunerDeviceId tunerDeviceId)
    {
        ArgumentNullException.ThrowIfNull(tunerDeviceId);
        RefuseUnlessInFlight();

        TunerDeviceId = tunerDeviceId;
    }

    /// <summary>
    /// Takes what the session writing this recording has counted so far. A session that opened at another moment
    /// than the one counted before is a new one: what was counted until then is carried, and what a session counts
    /// is added to what is carried. A reading that does not say when its session opened is of the session already
    /// being counted.
    /// </summary>
    public void Measure(
        DropCounters counters,
        DropTimeline positions,
        long? scrambledPackets,
        long eovfCount,
        DateTime at,
        DateTime? sessionOpenedAt = null)
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

        DateTime? opened = ToTheMillisecond(UtcTimes.Optional(sessionOpenedAt, nameof(sessionOpenedAt)))
                           ?? CountedSessionOpenedAt;
        CarriedCount carried = CountedSessionOpenedAt is { } counted && counted != opened
            ? new CarriedCount(Counters, Positions, ScrambledPackets, EovfCount)
            : Carried;
        DropCounters whole = Sum(carried.Counters, counters);
        TimeSpan into = opened is { } session && session > StartedAtActual ? session - StartedAtActual : TimeSpan.Zero;

        CarriedCcDroppedPackets = carried.Counters.Dropped;
        CarriedCcTotalPackets = carried.Counters.Total;
        CarriedPositions = carried.Positions;
        CarriedScrambledPackets = carried.ScrambledPackets;
        CarriedEovfCount = carried.Overflows;
        CountedSessionOpenedAt = opened;
        CcMeasured = whole.Measured;
        CcDroppedPackets = whole.Dropped;
        CcTotalPackets = whole.Total;
        Positions = carried.Positions.Then(positions, into);
        ScrambledPackets = Sum(carried.ScrambledPackets, scrambledPackets);
        EovfCount = carried.Overflows + eovfCount;
        MeasuredUpdatedAt = UtcTimes.Required(at, nameof(at));
    }

    private static DropCounters Sum(DropCounters before, DropCounters later)
    {
        if (!before.Measured)
        {
            return later;
        }

        return later.Measured
            ? DropCounters.Counted(
                before.Dropped.GetValueOrDefault() + later.Dropped.GetValueOrDefault(),
                before.Total.GetValueOrDefault() + later.Total.GetValueOrDefault())
            : before;
    }

    private static long? Sum(long? before, long? later)
        => before is null ? later : before + later.GetValueOrDefault();

    private static DateTime? ToTheMillisecond(DateTime? at)
        => at is { } moment ? moment.AddTicks(-(moment.Ticks % TimeSpan.TicksPerMillisecond)) : null;

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

    /// <summary>
    /// Keeps a gap a session carrying on into this recording's file left behind, and moves the interruption opened
    /// during that gap onto it. A gap that ends where one already kept ends is the same gap seen again, and is kept
    /// once.
    /// </summary>
    public void Missed(RecordingGap gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        RefuseUnlessInFlight();

        if (gaps.Any(kept => kept.Until == gap.Until))
        {
            return;
        }

        RefuseGapsThatDoNotFollowOneAnother([.. gaps, gap], StartedAtActual);

        gaps.Add(gap);
        MissedMs = MissedIn(gaps);
        PlaceTheInterruptionOn(gap);
    }

    /// <summary>
    /// Moves the last interruption that began before a gap ended and had not resumed before it began so that it
    /// begins where the gap begins and, when it has resumed, resumes where the gap ends.
    /// </summary>
    private void PlaceTheInterruptionOn(RecordingGap gap)
    {
        int index = interruptions.FindLastIndex(one => one.OccurredAt < gap.Until);

        if (index < 0 || (interruptions[index].ResumedAt is { } resumed && resumed <= gap.From))
        {
            return;
        }

        Interruption during = interruptions[index];
        DateTime previous = index is 0 ? StartedAtActual : interruptions[index - 1].ResumedAt!.Value;

        interruptions[index] = new Interruption(
            during.Fault,
            gap.From > previous ? gap.From : previous,
            during.ResumedAt is null ? null : gap.Until);
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

        if (DataBroadcastState is DataBroadcastState.None)
        {
            Keep(DataBroadcast.RecordingEnded(), null);
        }
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

    private static void RefuseALeftoverThatDoesNotAddUp(
        RecordingOutcome? outcome,
        DateTime? leftBehindAt,
        int? filesLeftBehind)
    {
        if (filesLeftBehind is not null && leftBehindAt is null)
        {
            throw new ArgumentException(
                "A count of files a deletion left behind says when that deletion was asked for.",
                nameof(leftBehindAt));
        }

        if (filesLeftBehind is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filesLeftBehind),
                filesLeftBehind,
                "A deletion that left files behind left at least one.");
        }

        if (leftBehindAt is not null && outcome is null)
        {
            throw new ArgumentException(
                "A recording still being written is never thrown away, so nothing was left behind by it.",
                nameof(leftBehindAt));
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

    private static void RefuseACarryBeyondTheCount(
        CarriedCount carried,
        DropCounters counters,
        long? scrambledPackets,
        long eovfCount)
    {
        RefuseAPositionNothingCounted(carried.Counters, carried.Positions, carried.ScrambledPackets);

        if (carried.Counters.Measured
            && (!counters.Measured || carried.Counters.Dropped > counters.Dropped || carried.Counters.Total > counters.Total))
        {
            throw new ArgumentException(
                "A recording counts what its earlier sessions counted and more, never less.",
                nameof(carried));
        }

        if (carried.ScrambledPackets > (scrambledPackets ?? 0) || carried.Overflows > eovfCount)
        {
            throw new ArgumentException(
                "A recording counts what its earlier sessions counted and more, never less.",
                nameof(carried));
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

    private static DataBroadcastProgress DueOnceEnded(RecordingOutcome? outcome)
        => outcome is null ? DataBroadcastProgress.NotYet : DataBroadcastProgress.NotYet.RecordingEnded();

    private static void RefuseADataBroadcastThatDoesNotAddUp(
        RecordingOutcome? outcome,
        DataBroadcastProgress progress,
        DateTime? madeAt)
    {
        if (progress.State is not DataBroadcastState.None && outcome is null)
        {
            throw new ArgumentException(
                "The data broadcast is taken from a recording once it has ended, never while it is being written.",
                nameof(outcome));
        }

        if (progress.State is DataBroadcastState.None or DataBroadcastState.Coming != madeAt is null)
        {
            throw new ArgumentException(
                "A record of the data broadcast that was taken, found missing or failed says when, and one not yet due or coming does not.",
                nameof(madeAt));
        }
    }

    private DataBroadcastProgress DueAgain()
        => DataBroadcastState switch
        {
            DataBroadcastState.None when !IsInFlight => DataBroadcast.RecordingEnded(),
            DataBroadcastState.Made => DataBroadcast.Lost(),
            _ => DataBroadcast.Retried(),
        };

    private DateTime Settled(DateTime at)
    {
        DateTime settled = UtcTimes.Required(at, nameof(at));

        RefuseATimeBeforeTheRecordingBegan(StartedAtActual, settled, nameof(at));

        return settled;
    }

    private void Keep(DataBroadcastProgress progress, DateTime? madeAt)
    {
        DataBroadcastState = progress.State;
        DataBroadcastModules = progress.Modules;
        DataBroadcastAttempts = progress.Attempts;
        DataBroadcastMadeAt = madeAt;
    }

    private static void RefuseCaptionsThatDoNotAddUp(CaptionState captionState, int? captionPictures)
    {
        if (!Enum.IsDefined(captionState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(captionState),
                captionState,
                "Captions are in one of the four states the ledger holds.");
        }

        if (captionState is CaptionState.Ready != captionPictures is not null)
        {
            throw new ArgumentException(
                "Captions that are ready say how many changes they keep, and nothing else counts any.",
                nameof(captionPictures));
        }

        if (captionPictures is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(captionPictures),
                captionPictures,
                "Captions that kept nothing are absent rather than ready.");
        }
    }

    private static void RefuseCaptionsKeptThatDoNotAddUp(
        RecordingOutcome? outcome,
        CaptionState captionState,
        DateTime? captionsMadeAt,
        int captionAttempts)
    {
        if (captionState is not CaptionState.Pending && outcome is null)
        {
            throw new ArgumentException(
                "Captions are taken from a recording once it has ended, never while it is being written.",
                nameof(captionState));
        }

        if (captionState is CaptionState.Pending != captionsMadeAt is null)
        {
            throw new ArgumentException(
                "Captions that were taken say when, and captions still waiting were not taken.",
                nameof(captionsMadeAt));
        }

        if (captionAttempts < 0 || captionState is CaptionState.Failed != captionAttempts > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(captionAttempts),
                captionAttempts,
                "Only captions that failed count the times in a row they failed.");
        }
    }

    private static long MissedIn(IReadOnlyList<RecordingGap> gaps)
        => gaps.Sum(gap => (long)Math.Ceiling(gap.Lasts.TotalMilliseconds));

    private static void RefuseGapsThatDoNotFollowOneAnother(IReadOnlyList<RecordingGap> gaps, DateTime startedAtActual)
    {
        DateTime previous = startedAtActual;

        foreach (RecordingGap gap in gaps)
        {
            if (gap.From < previous)
            {
                throw new ArgumentException(
                    "Gaps fall after the recording began, in the order they happened, and none of them overlap.",
                    nameof(gaps));
            }

            previous = gap.Until;
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

    private static bool EndedScrambled(IReadOnlyList<OutcomeDetail> detail)
        => detail.Any(one => one.Fault is RecordingFault.ScramblingUnresolved);

    private static void RefuseADescramblingThatDoesNotAddUp(
        RecordingOutcome? outcome,
        IReadOnlyList<OutcomeDetail> detail,
        DateTime? stoppedAtActual,
        DateTime? descrambledAt)
    {
        if (descrambledAt is not { } descrambled)
        {
            return;
        }

        if (outcome is null || !EndedScrambled(detail))
        {
            throw new ArgumentException(
                "Only a recording that ended with its scrambling unresolved says when it was descrambled.",
                nameof(descrambledAt));
        }

        if (stoppedAtActual is { } stopped && descrambled < stopped)
        {
            throw new ArgumentException(
                $"A recording is descrambled after it stopped at {stopped:O}.",
                nameof(descrambledAt));
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
