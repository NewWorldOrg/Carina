using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Notifications;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NoticeSourceTests
{
    private static readonly DateTime Began = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = Began.AddMinutes(30);

    [Fact(DisplayName = "a recording still running is told of once, as having started")]
    public void ARecordingStillRunningHasOnlyStarted()
    {
        RecordingNoticeSource recording = Recording(reserved: true, null);

        NoticeHappening happening = Assert.Single(recording.Happenings());

        Assert.Equal(NoticeKind.RecordingStarted, happening.Kind);
        Assert.Equal(recording.RecordingId, happening.SourceId);
        Assert.Equal(Began, happening.OccurredAt);
    }

    [Theory(DisplayName = "a recording that ended whole or cut short is told of as having started and as how it ended")]
    [InlineData(RecordingOutcome.Complete, NoticeKind.RecordingCompleted)]
    [InlineData(RecordingOutcome.Truncated, NoticeKind.RecordingTruncated)]
    public void ARecordingThatEndedIsToldOfTwice(RecordingOutcome outcome, NoticeKind kind)
    {
        RecordingNoticeSource recording = Recording(reserved: true, outcome);

        IReadOnlyList<NoticeHappening> happenings = [.. recording.Happenings()];

        Assert.Equal([NoticeKind.RecordingStarted, kind], happenings.Select(happening => happening.Kind));
        Assert.Equal(Ended, happenings[1].OccurredAt);
    }

    [Fact(DisplayName = "BR-ND-005: a failed recording with no reservation is told of as a failed recording")]
    public void AFailedRecordingWithNoReservationIsToldOf()
    {
        RecordingNoticeSource recording = Recording(reserved: false, RecordingOutcome.Failed);

        Assert.Contains(recording.Happenings(), happening => happening.Kind is NoticeKind.RecordingFailed);
    }

    [Fact(DisplayName = "BR-ND-005: a failed recording that has a reservation is left to the outcome of the reservation")]
    public void AFailedRecordingThatHasAReservationIsLeftToTheOutcome()
    {
        RecordingNoticeSource recording = Recording(reserved: true, RecordingOutcome.Failed);

        Assert.Equal([NoticeKind.RecordingStarted], recording.Happenings().Select(happening => happening.Kind));
    }

    [Theory(DisplayName = "BR-ND-005: a recording that ended is told of once as how it ended, from the recording or from the outcome of its reservation")]
    [InlineData(false, RecordingOutcome.Complete, false, NoticeKind.RecordingCompleted)]
    [InlineData(false, RecordingOutcome.Truncated, false, NoticeKind.RecordingTruncated)]
    [InlineData(false, RecordingOutcome.Failed, false, NoticeKind.RecordingFailed)]
    [InlineData(false, RecordingOutcome.Complete, true, NoticeKind.RecordingFailed)]
    [InlineData(true, RecordingOutcome.Complete, false, NoticeKind.RecordingCompleted)]
    [InlineData(true, RecordingOutcome.Truncated, false, NoticeKind.RecordingTruncated)]
    [InlineData(true, RecordingOutcome.Truncated, true, NoticeKind.RecordingTruncated)]
    [InlineData(true, RecordingOutcome.Failed, false, NoticeKind.ReservationMissed)]
    [InlineData(true, RecordingOutcome.Complete, true, NoticeKind.ReservationMissed)]
    public void ARecordingThatEndedIsToldOfOnceAsHowItEnded(
        bool reserved,
        RecordingOutcome outcome,
        bool leftScrambled,
        NoticeKind told)
    {
        IReadOnlyList<RecordingFault> faults = leftScrambled ? [RecordingFault.ScramblingUnresolved] : [];
        RecordingNoticeSource recording = Recording(reserved, outcome) with { Faults = faults };
        IEnumerable<NoticeHappening> ofTheReservation = reserved && FailsItsReservation(outcome, leftScrambled)
            ? (Outcome(ReservationOutcomeKind.RecordingFailure) with { Recorded = outcome, Faults = faults }).Happenings()
            : [];

        IEnumerable<NoticeKind> endings = recording.Happenings()
            .Concat(ofTheReservation)
            .Select(happening => happening.Kind)
            .Where(kind => kind is not NoticeKind.RecordingStarted);

        Assert.Equal([told], endings);
    }

    [Theory(DisplayName = "BR-ND-005: the outcome of a reservation is told of by its kind")]
    [InlineData(ReservationOutcomeKind.Competing, NoticeKind.ReservationCompeting)]
    [InlineData(ReservationOutcomeKind.Missed, NoticeKind.ReservationMissed)]
    [InlineData(ReservationOutcomeKind.RecordingFailure, NoticeKind.ReservationMissed)]
    [InlineData(ReservationOutcomeKind.GaveUpRetrying, NoticeKind.ReservationMissed)]
    [InlineData(ReservationOutcomeKind.ProgrammeMoved, NoticeKind.ProgrammeMoved)]
    [InlineData(ReservationOutcomeKind.ProgrammeGone, NoticeKind.ProgrammeMoved)]
    public void TheOutcomeOfAReservationIsToldOfByItsKind(ReservationOutcomeKind outcome, NoticeKind kind)
    {
        ReservationOutcomeNoticeSource source = Outcome(outcome);

        NoticeHappening happening = Assert.Single(source.Happenings());

        Assert.Equal(kind, happening.Kind);
        Assert.Equal(Ended, happening.OccurredAt);
    }

    [Fact(DisplayName = "BR-ND-004: the outcomes that say a reservation was not recorded share one key, whichever is read first")]
    public void TheOutcomesThatSayAReservationWasNotRecordedShareOneKey()
    {
        Guid reservation = Guid.NewGuid();
        IReadOnlyList<ReservationOutcomeNoticeSource> inTheOrderWritten =
        [
            Outcome(ReservationOutcomeKind.Missed) with { ReservationId = reservation },
            Outcome(ReservationOutcomeKind.RecordingFailure) with { ReservationId = reservation },
            Outcome(ReservationOutcomeKind.GaveUpRetrying) with { ReservationId = reservation },
        ];

        Assert.Equal([new NoticeKey(NoticeKind.ReservationMissed, reservation)], KeysOf(inTheOrderWritten).Distinct());
        Assert.Equal([new NoticeKey(NoticeKind.ReservationMissed, reservation)], KeysOf([.. inTheOrderWritten.Reverse()]).Distinct());
    }

    [Theory(DisplayName = "BR-ND-004: a reservation that lost its tuner and a programme that moved or went are keyed by their own row")]
    [InlineData(ReservationOutcomeKind.Competing)]
    [InlineData(ReservationOutcomeKind.ProgrammeMoved)]
    [InlineData(ReservationOutcomeKind.ProgrammeGone)]
    public void OtherOutcomesAreKeyedByTheirOwnRow(ReservationOutcomeKind outcome)
    {
        ReservationOutcomeNoticeSource source = Outcome(outcome);

        NoticeHappening happening = Assert.Single(source.Happenings());

        Assert.Equal(source.OutcomeId, happening.SourceId);
    }

    [Theory(DisplayName = "BR-ND-005: a retry, a failure to tune and a programme that came back are not told of")]
    [InlineData(ReservationOutcomeKind.Retried)]
    [InlineData(ReservationOutcomeKind.TuneFailure)]
    [InlineData(ReservationOutcomeKind.ProgrammeReturned)]
    public void SomeOutcomesAreNotToldOf(ReservationOutcomeKind outcome)
    {
        Assert.Empty(Outcome(outcome).Happenings());
    }

    [Fact]
    public void EveryKindOfOutcomeIsEitherToldOfOrLeftOutOnPurpose()
    {
        IReadOnlyList<ReservationOutcomeKind> leftOut =
        [
            ReservationOutcomeKind.Retried,
            ReservationOutcomeKind.TuneFailure,
            ReservationOutcomeKind.ProgrammeReturned,
        ];

        Assert.All(
            Enum.GetValues<ReservationOutcomeKind>().Except(leftOut),
            kind => Assert.Single(Outcome(kind).Happenings()));
    }

    [Fact(DisplayName = "an encode still waiting is not told of, and one running is told of as having started")]
    public void AnEncodeIsToldOfOnceItStarts()
    {
        Assert.Empty(Encode(EncodeJobStatus.Queued, null, null).Happenings());

        NoticeHappening happening = Assert.Single(Encode(EncodeJobStatus.Running, Began, null).Happenings());

        Assert.Equal(NoticeKind.EncodeStarted, happening.Kind);
        Assert.Equal(Began, happening.OccurredAt);
    }

    [Theory(DisplayName = "an encode that finished or failed is told of as how it ended")]
    [InlineData(EncodeJobStatus.Completed, NoticeKind.EncodeCompleted)]
    [InlineData(EncodeJobStatus.Failed, NoticeKind.EncodeFailed)]
    public void AnEncodeThatEndedIsToldOfAsHowItEnded(EncodeJobStatus status, NoticeKind kind)
    {
        IReadOnlyList<NoticeHappening> happenings = [.. Encode(status, Began, Ended).Happenings()];

        Assert.Equal([NoticeKind.EncodeStarted, kind], happenings.Select(happening => happening.Kind));
        Assert.Equal(Ended, happenings[1].OccurredAt);
    }

    [Fact(DisplayName = "an encode that was cancelled is not told of as having ended")]
    public void ACancelledEncodeIsNotToldOfAsHavingEnded()
    {
        Assert.Empty(Encode(EncodeJobStatus.Cancelled, null, Ended).Happenings());
        Assert.Equal(
            [NoticeKind.EncodeStarted],
            Encode(EncodeJobStatus.Cancelled, Began, Ended).Happenings().Select(happening => happening.Kind));
    }

    [Fact(DisplayName = "BR-ND-006: a quality incident is told of once when it is detected and once when it is resolved")]
    public void AQualityIncidentIsToldOfWhenDetectedAndWhenResolved()
    {
        Assert.Equal(
            [NoticeKind.QualityIncidentDetected],
            Quality(QualityIncidentOwner.Quality, null).Happenings().Select(happening => happening.Kind));

        IReadOnlyList<NoticeHappening> happenings = [.. Quality(QualityIncidentOwner.Quality, Ended).Happenings()];

        Assert.Equal([NoticeKind.QualityIncidentDetected, NoticeKind.QualityIncidentResolved], happenings.Select(happening => happening.Kind));
        Assert.Equal([Began, Ended], happenings.Select(happening => happening.OccurredAt));
    }

    [Fact(DisplayName = "a tuner in trouble is told of once, as a device that was lost")]
    public void ATunerInTroubleIsToldOfOnce()
    {
        NoticeHappening happening = Assert.Single(Quality(QualityIncidentOwner.Tuner, Ended).Happenings());

        Assert.Equal(NoticeKind.DeviceDisconnected, happening.Kind);
        Assert.Equal(Began, happening.OccurredAt);
    }

    [Theory(DisplayName = "an incident restated from the guide, a reservation or a recording is not told of")]
    [InlineData(QualityIncidentOwner.Guide)]
    [InlineData(QualityIncidentOwner.Reservation)]
    [InlineData(QualityIncidentOwner.Recording)]
    public void OtherRestatedIncidentsAreNotToldOf(QualityIncidentOwner owner)
    {
        Assert.Empty(Quality(owner, Ended).Happenings());
    }

    private static RecordingNoticeSource Recording(bool reserved, RecordingOutcome? outcome)
        => new(
            Guid.NewGuid(),
            reserved,
            "A programme",
            "A station",
            Began,
            Ended,
            Began,
            outcome is null ? null : Ended,
            outcome,
            0,
            []);

    private static ReservationOutcomeNoticeSource Outcome(ReservationOutcomeKind kind)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            "A programme",
            "A station",
            Began,
            Ended,
            Ended,
            kind is ReservationOutcomeKind.RecordingFailure ? RecordingOutcome.Failed : null,
            [],
            null,
            []);

    private static bool FailsItsReservation(RecordingOutcome outcome, bool leftScrambled)
        => outcome is RecordingOutcome.Failed or RecordingOutcome.Truncated
            || (outcome is RecordingOutcome.Complete && leftScrambled);

    private static IEnumerable<NoticeKey> KeysOf(IReadOnlyList<ReservationOutcomeNoticeSource> outcomes)
        => outcomes
            .SelectMany(outcome => outcome.Happenings())
            .Select(happening => new NoticeKey(happening.Kind, happening.SourceId));

    private static EncodeNoticeSource Encode(EncodeJobStatus status, DateTime? startedAt, DateTime? endedAt)
        => new(Guid.NewGuid(), Guid.NewGuid(), "A programme", EncodeCodec.H265, status, startedAt, endedAt, null, null, null);

    private static QualityNoticeSource Quality(QualityIncidentOwner owner, DateTime? resolvedAt)
        => new(
            Guid.NewGuid(),
            owner,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubjectKind.Recording,
            2.5,
            1,
            owner is QualityIncidentOwner.Tuner ? TunerTroubleKind.NoLock : null,
            Began,
            resolvedAt);
}
