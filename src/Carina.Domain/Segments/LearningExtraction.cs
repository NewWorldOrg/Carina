using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// The record of taking the learning data out of one recording: where it stands, how far it read,
/// what it could not read and why it stopped, beside the copy of the programme. It names the
/// recording by value.
/// </summary>
public sealed class LearningExtraction
{
    public const int MostRetries = 3;

    private static readonly LearningExtractionState[] Running = [LearningExtractionState.Following, LearningExtractionState.Reading];

    private List<LearningDataGap> gaps = [];

    private LearningExtraction()
    {
    }

    public RecordingId RecordingId { get; private set; } = null!;

    public LearningExtractionState State { get; private set; }

    public ExtractionVersion? Version { get; private set; }

    public TimeSpan ReadThrough { get; private set; }

    public IReadOnlyList<LearningDataGap> Gaps
    {
        get => gaps;
        private set => gaps = [.. value];
    }

    public ExtractionSound? Sound { get; private set; }

    public ExtractionFailureDetail? Failure { get; private set; }

    public int Failures { get; private set; }

    public ProgrammeCopy Programme { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// When what the record holds was last matched against the catalog; null when it never was.
    /// </summary>
    public DateTime? MatchedAt { get; private set; }

    public bool CanRetry => State is LearningExtractionState.Failed && Failures <= MostRetries;

    /// <summary>
    /// Whether what the record holds waits to be matched against the catalog: it was read to its end or as far
    /// as it could be, and has not been matched since it last changed.
    /// </summary>
    public bool AwaitsMatching
        => State is LearningExtractionState.Done or LearningExtractionState.Partial && (MatchedAt is null || MatchedAt < UpdatedAt);

    private bool MadeFromAReducedCopy => Version?.Origin is ExtractionOrigin.ReducedCopy;

    public static LearningExtraction Following(
        RecordingId recordingId,
        ProgrammeCopy programme,
        ExtractionVersion version,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(version);

        return Rehydrate(recordingId, LearningExtractionState.Following, version, TimeSpan.Zero, [], null, null, 0, programme, at, at);
    }

    public static LearningExtraction Waiting(RecordingId recordingId, ProgrammeCopy programme, DateTime at)
        => Rehydrate(recordingId, LearningExtractionState.Waiting, null, TimeSpan.Zero, [], null, null, 0, programme, at, at);

    public static LearningExtraction Rehydrate(
        RecordingId recordingId,
        LearningExtractionState state,
        ExtractionVersion? version,
        TimeSpan readThrough,
        IReadOnlyList<LearningDataGap> gaps,
        ExtractionSound? sound,
        ExtractionFailureDetail? failure,
        int failures,
        ProgrammeCopy programme,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? matchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentOutOfRangeException.ThrowIfLessThan(readThrough, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(failures);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "An extraction stands in one of the six states kept.");
        }

        string? fault = Fault(state, version, failure, failures) ?? Unordered(gaps);

        if (fault is not null)
        {
            throw new ArgumentException(fault);
        }

        DateTime created = UtcTimes.Required(createdAt, nameof(createdAt));
        DateTime updated = UtcTimes.Required(updatedAt, nameof(updatedAt));

        if (updated < created)
        {
            throw new ArgumentException("An extraction is changed no earlier than it was made.", nameof(updatedAt));
        }

        DateTime? matched = UtcTimes.Optional(matchedAt, nameof(matchedAt));

        if (matched < created)
        {
            throw new ArgumentException("An extraction is matched no earlier than it was made.", nameof(matchedAt));
        }

        return new LearningExtraction
        {
            RecordingId = recordingId,
            State = state,
            Version = version,
            ReadThrough = readThrough,
            Gaps = gaps,
            Sound = sound,
            Failure = failure,
            Failures = failures,
            Programme = programme,
            CreatedAt = created,
            UpdatedAt = updated,
            MatchedAt = matched,
        };
    }

    /// <summary>
    /// Marks what the record holds as matched against the catalog, leaving when the record last changed as it
    /// was. A moment before that counts as that moment.
    /// </summary>
    public void Matched(DateTime at)
    {
        DateTime when = UtcTimes.Required(at, nameof(at));

        if (!AwaitsMatching)
        {
            throw new InvalidOperationException($"An extraction that is {State}, or already matched since it last changed, does not wait to be matched.");
        }

        MatchedAt = when > UpdatedAt ? when : UpdatedAt;
    }

    /// <summary>
    /// Takes what the record holds as matched again at <paramref name="at"/>, the catalog having changed under it, so
    /// that its detection is made again (<see cref="RecordingDetection.IsAwaited"/>); false, changing nothing, when it
    /// waits to be matched, which has it detected again anyway, or was matched no earlier than that.
    /// </summary>
    public bool CatalogChanged(DateTime at)
    {
        DateTime when = UtcTimes.Required(at, nameof(at));

        if (AwaitsMatching || MatchedAt is not DateTime matched || matched >= when)
        {
            return false;
        }

        MatchedAt = when;

        return true;
    }

    public void Follow(ExtractionVersion version, DateTime at)
    {
        DateTime when = Moving(at, "is followed", LearningExtractionState.Waiting);

        Restart(LearningExtractionState.Following, version, when);
    }

    public void Read(ExtractionVersion version, DateTime at)
    {
        DateTime when = Moving(at, "is read", LearningExtractionState.Waiting);

        Restart(LearningExtractionState.Reading, version, when);
    }

    /// <summary>
    /// Whether the learning data of a recording that ended at <paramref name="recordingEnded"/> waits to be
    /// read from its file: the record waits, failed with a try left, or holds data made another way than
    /// <paramref name="current"/> or left partway before the recording ended.
    /// </summary>
    public bool AwaitsReading(ExtractionVersion current, DateTime? recordingEnded)
    {
        ArgumentNullException.ThrowIfNull(current);

        return State switch
        {
            LearningExtractionState.Waiting => true,
            LearningExtractionState.Failed => CanRetry,
            LearningExtractionState.Done => Version != current,
            LearningExtractionState.Partial => Version != current || UpdatedAt < recordingEnded,
            _ => false,
        };
    }

    /// <summary>
    /// Reads from its head, with <paramref name="current"/>, a recording whose learning data
    /// <see cref="AwaitsReading"/>.
    /// </summary>
    public void ReadAwaited(ExtractionVersion current, DateTime? recordingEnded, DateTime at)
    {
        if (!AwaitsReading(current, recordingEnded))
        {
            throw new InvalidOperationException($"An extraction that is {State} does not wait to be read.");
        }

        switch (State)
        {
            case LearningExtractionState.Failed:
                Retry(at);
                break;
            case LearningExtractionState.Done:
                Outdate(current, at);
                break;
            case LearningExtractionState.Partial:
                Reopen(at);
                break;
        }

        Read(current, at);
    }

    /// <summary>
    /// Whether the learning data waits to be imported from a reduced copy of the recording with
    /// <paramref name="reduced"/>: the record waits, failed reading the file, failed importing with a try
    /// left, was left partway reading the file, or holds data made from a reduced copy by other
    /// calculations than <paramref name="reduced"/>. Data read from the file is not imported over.
    /// </summary>
    public bool AwaitsImport(ExtractionVersion reduced)
    {
        FromAReducedCopy(reduced);

        return State switch
        {
            LearningExtractionState.Waiting => true,
            LearningExtractionState.Failed => CanRetry || !MadeFromAReducedCopy,
            LearningExtractionState.Done => MadeFromAReducedCopy && Version!.Number != reduced.Number,
            LearningExtractionState.Partial => !MadeFromAReducedCopy || Version!.Number != reduced.Number,
            _ => false,
        };
    }

    /// <summary>
    /// Reads from its head, with <paramref name="reduced"/>, a reduced copy of a recording whose learning
    /// data <see cref="AwaitsImport"/>.
    /// </summary>
    public void Import(ExtractionVersion reduced, DateTime at)
    {
        if (!AwaitsImport(reduced))
        {
            throw new InvalidOperationException($"An extraction that is {State} does not wait to be imported.");
        }

        Restart(LearningExtractionState.Reading, reduced, UtcTimes.Required(at, nameof(at)));
    }

    public void Opened(ExtractionSound sound, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(sound);

        DateTime when = Moving(at, "takes a sound", Running);

        Sound = sound;
        Touch(when);
    }

    public void Reached(TimeSpan readThrough, DateTime at)
    {
        DateTime when = Moving(at, "reads on", Running);

        if (readThrough < ReadThrough)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readThrough),
                readThrough,
                $"Reading only goes forwards, and this one has read to {ReadThrough}.");
        }

        ReadThrough = readThrough;
        Touch(when);
    }

    public void Missed(LearningDataGap gap, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(gap);

        DateTime when = Moving(at, "finds a gap", Running);

        if (gaps.Count > 0 && gap.From < gaps[^1].Until)
        {
            throw new ArgumentException("Gaps are found in the order they fall, and do not overlap.", nameof(gap));
        }

        gaps.Add(gap);
        Touch(when);
    }

    public void Finish(DateTime at) => Settle(LearningExtractionState.Done, "is read to the end", at);

    public void FinishPartway(DateTime at) => Settle(LearningExtractionState.Partial, "stops partway", at);

    public void Fail(ExtractionFailure failure, string reason, DateTime at)
    {
        DateTime when = Moving(at, "fails", Running);
        var detail = new ExtractionFailureDetail(failure, reason);

        State = LearningExtractionState.Failed;
        Failure = detail;
        Failures++;
        Touch(when);
    }

    public void Pause(DateTime at)
    {
        DateTime when = Moving(at, "is put back to wait", Running);

        State = LearningExtractionState.Waiting;
        Touch(when);
    }

    public void Outdate(ExtractionVersion current, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(current);

        DateTime when = Moving(at, "waits to be read again for a new way of reading", LearningExtractionState.Done);

        if (current == Version)
        {
            throw new InvalidOperationException("Data made the way it is made now is not read again.");
        }

        State = LearningExtractionState.Waiting;
        Touch(when);
    }

    public void Reopen(DateTime at)
    {
        DateTime when = Moving(at, "waits to be read again", LearningExtractionState.Partial);

        State = LearningExtractionState.Waiting;
        Touch(when);
    }

    public void Retry(DateTime at)
    {
        DateTime when = Moving(at, "is tried again", LearningExtractionState.Failed);

        if (!CanRetry)
        {
            throw new InvalidOperationException($"A failed extraction is tried again at most {MostRetries} times.");
        }

        State = LearningExtractionState.Waiting;
        Touch(when);
    }

    public void Recover(bool stillRecording, ExtractionVersion version, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(version);

        DateTime when = Moving(at, "is picked up again after a start", Running);

        if (stillRecording)
        {
            Restart(LearningExtractionState.Following, version, when);

            return;
        }

        State = LearningExtractionState.Waiting;
        Touch(when);
    }

    private static string? Fault(
        LearningExtractionState state,
        ExtractionVersion? version,
        ExtractionFailureDetail? failure,
        int failures)
    {
        if (version is null && state is not LearningExtractionState.Waiting)
        {
            return "Only an extraction that has not been read is without a version.";
        }

        if ((failure is null) != (failures == 0))
        {
            return "The last failure is kept for as long as failures are counted.";
        }

        if (state is LearningExtractionState.Failed && failure is null)
        {
            return "A failed extraction says why.";
        }

        return state is LearningExtractionState.Done or LearningExtractionState.Partial && failures > 0
            ? "An extraction that read through counts no failures."
            : null;
    }

    private static void FromAReducedCopy(ExtractionVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Origin is not ExtractionOrigin.ReducedCopy)
        {
            throw new ArgumentException("A reduced copy is imported with a version made from a reduced copy.", nameof(version));
        }
    }

    private static string? Unordered(IReadOnlyList<LearningDataGap> gaps)
    {
        for (int at = 1; at < gaps.Count; at++)
        {
            if (gaps[at].From < gaps[at - 1].Until)
            {
                return "Gaps are kept in the order they fall, and do not overlap.";
            }
        }

        return null;
    }

    private DateTime Moving(DateTime at, string what, params LearningExtractionState[] from)
    {
        DateTime when = UtcTimes.Required(at, nameof(at));

        if (!from.Contains(State))
        {
            throw new InvalidOperationException(
                $"An extraction {what} only when it is {string.Join(" or ", from)}, and this one is {State}.");
        }

        return when;
    }

    private void Settle(LearningExtractionState state, string what, DateTime at)
    {
        DateTime when = Moving(at, what, Running);

        State = state;
        Failure = null;
        Failures = 0;
        Touch(when);
    }

    private void Restart(LearningExtractionState state, ExtractionVersion version, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(version);

        State = state;
        Version = version;
        ReadThrough = TimeSpan.Zero;
        gaps = [];
        Sound = null;
        Touch(at);
    }

    private void Touch(DateTime at)
    {
        if (at > UpdatedAt)
        {
            UpdatedAt = at;
        }
    }
}
