using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Notifications;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationIntakeRoundTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(15);

    private readonly NoticeLedgers ledgers = new(database);

    [Fact(DisplayName = "BR-ND-004: a look after a restart over the same rows takes nothing more")]
    public async Task ALookAfterARestartOverTheSameRowsTakesNothingMore()
    {
        DateTime now = await ADayOfItsOwnAsync(1);
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", now);
        await ledgers.RuleAsync(
            "Recordings",
            now,
            [NoticeKind.RecordingStarted, NoticeKind.RecordingCompleted],
            [destination]);
        await ledgers.LookAsync(now);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Complete, now.AddMinutes(-1));

        NotificationIntake first = await ledgers.LookAsync(now);
        NotificationIntake second = await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal(2, first.Taken);
        Assert.Equal(2, first.Queued);
        Assert.Equal(0, second.Taken);
        Assert.Equal(0, second.Queued);
        Assert.Equal(
            [NoticeKind.RecordingStarted, NoticeKind.RecordingCompleted],
            await ledgers.KindsOfAsync(recording.Value));
        Assert.Single(await ledgers.DeliveriesOfAsync(recording.Value, NoticeKind.RecordingStarted));
        Assert.Single(await ledgers.DeliveriesOfAsync(recording.Value, NoticeKind.RecordingCompleted));
    }

    [Fact(DisplayName = "BR-ND-004: a notice is not written when one of its deliveries cannot be")]
    public async Task ANoticeIsNotWrittenWhenOneOfItsDeliveriesCannotBe()
    {
        DateTime now = await ADayOfItsOwnAsync(2);
        Guid source = Guid.NewGuid();
        Notice notice = ANotice(source, now);
        NotificationDelivery nowhere = NotificationDelivery.Queue(
            NotificationDeliveryId.New(),
            notice.Id,
            NotificationDestinationId.New(),
            now);

        await using CarinaDbContext context = database.Open();
        await Assert.ThrowsAsync<DbUpdateException>(
            () => new NoticeRepository(context).AddAsync(notice, [nowhere], Cancel));

        Assert.Empty(await ledgers.NoticesOfAsync(source));
    }

    [Fact(DisplayName = "BR-ND-004: a second notice of the same kind from the same row is refused, with its deliveries")]
    public async Task ASecondNoticeOfTheSameKindFromTheSameRowIsRefusedWithItsDeliveries()
    {
        DateTime now = await ADayOfItsOwnAsync(3);
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", now);
        Guid source = Guid.NewGuid();
        Notice first = ANotice(source, now);
        Notice again = ANotice(source, now);

        await using CarinaDbContext context = database.Open();
        NoticeRepository notices = new(context);
        bool written = await notices.AddAsync(first, [ADelivery(first, destination, now)], Cancel);
        bool writtenAgain = await notices.AddAsync(again, [ADelivery(again, destination, now)], Cancel);

        Assert.True(written);
        Assert.False(writtenAgain);
        Notice held = Assert.Single(await ledgers.NoticesOfAsync(source));
        Assert.Equal(first.Id, held.Id);
        Assert.Single(await ledgers.DeliveriesOfAsync(source, NoticeKind.RecordingStarted));
        Assert.Contains(new NoticeKey(NoticeKind.RecordingStarted, source), await notices.HeldAmongAsync([source], Cancel));
    }

    [Fact(DisplayName = "BR-ND-004: a notice that could not be written is not written by the next write either")]
    public async Task ANoticeThatCouldNotBeWrittenIsNotWrittenByTheNextWriteEither()
    {
        DateTime now = await ADayOfItsOwnAsync(25);
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", now);
        Notice unwritten = ANotice(Guid.NewGuid(), now);
        NotificationDelivery nowhere = ADelivery(unwritten, NotificationDestinationId.New(), now);
        Notice next = ANotice(Guid.NewGuid(), now);

        await using CarinaDbContext context = database.Open();
        NoticeRepository notices = new(context);
        await Assert.ThrowsAsync<DbUpdateException>(() => notices.AddAsync(unwritten, [nowhere], Cancel));
        bool written = await notices.AddAsync(next, [ADelivery(next, destination, now)], Cancel);

        Assert.True(written);
        Assert.Empty(await ledgers.NoticesOfAsync(unwritten.SourceId));
        Assert.Single(await ledgers.NoticesOfAsync(next.SourceId));
        Assert.Single(await ledgers.DeliveriesOfAsync(next.SourceId, NoticeKind.RecordingStarted));
    }

    [Fact(DisplayName = "BR-ND-004: a notice refused as already held does not keep the next one from being written")]
    public async Task ANoticeRefusedAsAlreadyHeldDoesNotKeepTheNextOneFromBeingWritten()
    {
        DateTime now = await ADayOfItsOwnAsync(26);
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", now);
        Guid source = Guid.NewGuid();
        Notice first = ANotice(source, now);
        Notice again = ANotice(source, now);
        Notice next = ANotice(Guid.NewGuid(), now);

        await using CarinaDbContext context = database.Open();
        NoticeRepository notices = new(context);
        await notices.AddAsync(first, [ADelivery(first, destination, now)], Cancel);
        bool writtenAgain = await notices.AddAsync(again, [ADelivery(again, destination, now)], Cancel);
        bool written = await notices.AddAsync(next, [ADelivery(next, destination, now)], Cancel);

        Assert.False(writtenAgain);
        Assert.True(written);
        Assert.Single(await ledgers.NoticesOfAsync(source));
        Assert.Single(await ledgers.DeliveriesOfAsync(source, NoticeKind.RecordingStarted));
        Assert.Single(await ledgers.DeliveriesOfAsync(next.SourceId, NoticeKind.RecordingStarted));
    }

    [Fact(DisplayName = "BR-ND-005: a failed recording that has a reservation is told of once, from the reservation's outcome")]
    public async Task AFailedRecordingThatHasAReservationIsToldOfOnceFromTheReservationsOutcome()
    {
        DateTime now = await ADayOfItsOwnAsync(4);
        ReservationId reservation = ReservationId.New();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20), reservation);
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Failed, now.AddMinutes(-5));
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.RecordingFailure,
            now.AddMinutes(-5),
            reservation,
            recordingOutcome: RecordingOutcome.Failed,
            faults: [RecordingFault.ShortOfTheWindow]);

        await ledgers.LookAsync(now);
        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal([NoticeKind.RecordingStarted], await ledgers.KindsOfAsync(recording.Value));
        Assert.Equal([NoticeKind.ReservationMissed], await ledgers.KindsOfAsync(reservation.Value));
    }

    [Fact(DisplayName = "BR-ND-005: a recording cut short that has a reservation is told of once, from the recording")]
    public async Task ARecordingCutShortThatHasAReservationIsToldOfOnceFromTheRecording()
    {
        DateTime now = await ADayOfItsOwnAsync(18);
        ReservationId reservation = ReservationId.New();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20), reservation);
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Truncated, now.AddMinutes(-5));
        ReservationOutcomeId outcome = await ledgers.OutcomeAsync(
            ReservationOutcomeKind.RecordingFailure,
            now.AddMinutes(-5),
            reservation,
            recordingOutcome: RecordingOutcome.Truncated,
            faults: [RecordingFault.ShortOfTheWindow]);

        await ledgers.LookAsync(now);
        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal(
            [NoticeKind.RecordingStarted, NoticeKind.RecordingTruncated],
            await ledgers.KindsOfAsync(recording.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(reservation.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(outcome.Value));
    }

    [Fact(DisplayName = "BR-ND-005: a recording that ran its length but stayed scrambled, with a reservation, is told of once as not recorded")]
    public async Task ARecordingThatStayedScrambledWithAReservationIsToldOfOnceAsNotRecorded()
    {
        DateTime now = await ADayOfItsOwnAsync(19);
        ReservationId reservation = ReservationId.New();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20), reservation);
        await ledgers.RecordingEndedScrambledAsync(recording, now.AddMinutes(-5));
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.RecordingFailure,
            now.AddMinutes(-5),
            reservation,
            recordingOutcome: RecordingOutcome.Complete,
            faults: [RecordingFault.ScramblingUnresolved]);

        await ledgers.LookAsync(now);
        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal([NoticeKind.RecordingStarted], await ledgers.KindsOfAsync(recording.Value));
        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(reservation.Value));
        Assert.Equal(NoticeKind.ReservationMissed, notice.Kind);
        Assert.Contains("スクランブルが解けなかった", notice.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-005: a recording that ran its length but stayed scrambled, with no reservation, is told of once as failed")]
    public async Task ARecordingThatStayedScrambledWithNoReservationIsToldOfOnceAsFailed()
    {
        DateTime now = await ADayOfItsOwnAsync(20);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20));
        await ledgers.RecordingEndedScrambledAsync(recording, now.AddMinutes(-5));

        await ledgers.LookAsync(now);
        await ledgers.LookAsync(now + BetweenLooks);

        IReadOnlyList<Notice> notices = await ledgers.NoticesOfAsync(recording.Value);
        Assert.Equal([NoticeKind.RecordingStarted, NoticeKind.RecordingFailed], notices.Select(notice => notice.Kind));
        Assert.Contains("スクランブルが解けなかった", notices[1].Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-004: a recording that failed and the retries given up after it are told of once for the reservation")]
    public async Task ARecordingThatFailedAndTheRetriesGivenUpAfterItAreToldOfOnce()
    {
        DateTime now = await ADayOfItsOwnAsync(21);
        ReservationId reservation = ReservationId.New();
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.RecordingFailure,
            now.AddMinutes(-5),
            reservation,
            recordingOutcome: RecordingOutcome.Failed,
            faults: [RecordingFault.ShortOfTheWindow]);

        await ledgers.LookAsync(now);
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.GaveUpRetrying,
            now.AddSeconds(5),
            reservation,
            gaveUpBecause: RetryGiveUp.BroadcastOver);
        await ledgers.LookAsync(now + BetweenLooks);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(reservation.Value));
        Assert.Equal(NoticeKind.ReservationMissed, notice.Kind);
        Assert.Contains("録画の失敗", notice.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-004: a reservation missed and given up on in the same look is told of once")]
    public async Task AReservationMissedAndGivenUpOnInTheSameLookIsToldOfOnce()
    {
        DateTime now = await ADayOfItsOwnAsync(22);
        ReservationId reservation = ReservationId.New();
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, now.AddMinutes(-4), reservation);
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.GaveUpRetrying,
            now.AddMinutes(-3),
            reservation,
            gaveUpBecause: RetryGiveUp.BroadcastOver);

        await ledgers.LookAsync(now);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(reservation.Value));
        Assert.Contains("取り逃し", notice.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-004: when the giving up is read first, the miss written late does not tell of the reservation again")]
    public async Task WhenTheGivingUpIsReadFirstTheMissWrittenLateDoesNotTellAgain()
    {
        DateTime now = await ADayOfItsOwnAsync(23);
        ReservationId reservation = ReservationId.New();
        await ledgers.OutcomeAsync(
            ReservationOutcomeKind.GaveUpRetrying,
            now.AddMinutes(-2),
            reservation,
            gaveUpBecause: RetryGiveUp.BroadcastOver);

        await ledgers.LookAsync(now);
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, now.AddMinutes(-4), reservation);
        NotificationIntake later = await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal(0, later.Taken);
        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(reservation.Value));
        Assert.Contains("再試行の打ち切り", notice.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-005: a failed recording that has no reservation is told of from the recording")]
    public async Task AFailedRecordingThatHasNoReservationIsToldOfFromTheRecording()
    {
        DateTime now = await ADayOfItsOwnAsync(5);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Failed, now.AddMinutes(-5));

        await ledgers.LookAsync(now);

        Assert.Equal(
            [NoticeKind.RecordingStarted, NoticeKind.RecordingFailed],
            await ledgers.KindsOfAsync(recording.Value));
    }

    [Fact(DisplayName = "BR-ND-005: a retry, a failure to tune and a programme that came back are not told of")]
    public async Task ARetryAFailureToTuneAndAProgrammeThatCameBackAreNotToldOf()
    {
        DateTime now = await ADayOfItsOwnAsync(6);
        ReservationOutcomeId retried = await ledgers.OutcomeAsync(
            ReservationOutcomeKind.Retried,
            now.AddMinutes(-3),
            retryResult: RetryResult.Started);
        ReservationOutcomeId untuned = await ledgers.OutcomeAsync(
            ReservationOutcomeKind.TuneFailure,
            now.AddMinutes(-3),
            tuneFailure: TuneFailureKind.NoLock,
            faults: [RecordingFault.TuneFailed]);
        ReservationOutcomeId returned = await ledgers.OutcomeAsync(ReservationOutcomeKind.ProgrammeReturned, now.AddMinutes(-3));
        ReservationId missed = ReservationId.New();
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, now.AddMinutes(-3), missed);

        await ledgers.LookAsync(now);

        Assert.Empty(await ledgers.NoticesOfAsync(retried.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(untuned.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(returned.Value));
        Assert.Equal([NoticeKind.ReservationMissed], await ledgers.KindsOfAsync(missed.Value));
    }

    [Fact(DisplayName = "BR-ND-006: reception that fell is told of once when it falls and once when it comes back")]
    public async Task ReceptionThatFellIsToldOfOnceWhenItFallsAndOnceWhenItComesBack()
    {
        DateTime now = await ADayOfItsOwnAsync(7);
        QualityIncidentId incident = await ledgers.ReceptionFellAsync(now.AddMinutes(-2));

        await ledgers.LookAsync(now);
        await ledgers.ShownOnTheScreenAsync(incident, now.AddSeconds(5));
        await ledgers.LookAsync(now + BetweenLooks);
        await ledgers.LookAsync(now.AddMinutes(10));

        Assert.Equal([NoticeKind.QualityIncidentDetected], await ledgers.KindsOfAsync(incident.Value));

        await ledgers.ReceptionCameBackAsync(incident, now.AddMinutes(11));
        await ledgers.LookAsync(now.AddMinutes(11) + BetweenLooks);
        await ledgers.LookAsync(now.AddMinutes(12));

        Assert.Equal(
            [NoticeKind.QualityIncidentDetected, NoticeKind.QualityIncidentResolved],
            await ledgers.KindsOfAsync(incident.Value));
    }

    [Fact(DisplayName = "A tuner that failed is told of once, as trouble with the device")]
    public async Task ATunerThatFailedIsToldOfOnceAsTroubleWithTheDevice()
    {
        DateTime now = await ADayOfItsOwnAsync(8);
        QualityIncidentId incident = await ledgers.TunerFailedToLockAsync(now.AddMinutes(-2));

        await ledgers.LookAsync(now);
        await ledgers.ReceptionCameBackAsync(incident, now.AddMinutes(1));
        await ledgers.LookAsync(now.AddMinutes(1) + BetweenLooks);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(incident.Value));
        Assert.Equal(NoticeKind.DeviceDisconnected, notice.Kind);
        Assert.DoesNotContain("synthetic-0", notice.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-010: a destination two rule sets name gets one delivery, and what is disabled gets none")]
    public async Task ADestinationTwoRuleSetsNameGetsOneDeliveryAndWhatIsDisabledGetsNone()
    {
        DateTime now = await ADayOfItsOwnAsync(9);
        NotificationDestinationId named = await ledgers.DestinationAsync("Named twice", now);
        NotificationDestinationId silenced = await ledgers.DestinationAsync("Named by a rule set that is off", now);
        NotificationDestinationId disabled = await ledgers.DestinationAsync("Turned off", now, enabled: false);
        await ledgers.RuleAsync("First", now, [NoticeKind.RecordingStarted], [named, disabled]);
        await ledgers.RuleAsync("Second", now, [NoticeKind.RecordingStarted, NoticeKind.RecordingCompleted], [named]);
        await ledgers.RuleAsync("Off", now, [NoticeKind.RecordingStarted], [silenced], enabled: false);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-1));

        await ledgers.LookAsync(now);

        NotificationDelivery delivery = Assert.Single(
            await ledgers.DeliveriesOfAsync(recording.Value, NoticeKind.RecordingStarted));
        Assert.Equal(named, delivery.DestinationId);
        Assert.Equal(NotificationDeliveryState.Queued, delivery.State);
        Assert.Equal(0, delivery.Attempts);
        Assert.Equal(now, delivery.QueuedAt);
    }

    [Fact(DisplayName = "A notice no rule set asks for is held with nothing to deliver")]
    public async Task ANoticeNoRuleSetAsksForIsHeldWithNothingToDeliver()
    {
        DateTime now = await ADayOfItsOwnAsync(10);
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", now);
        await ledgers.RuleAsync("Endings only", now, [NoticeKind.RecordingCompleted], [destination]);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Complete, now.AddMinutes(-1));

        await ledgers.LookAsync(now);

        Assert.Empty(await ledgers.DeliveriesOfAsync(recording.Value, NoticeKind.RecordingStarted));
        Assert.Single(await ledgers.DeliveriesOfAsync(recording.Value, NoticeKind.RecordingCompleted));
    }

    [Fact(DisplayName = "BR-NC-004: what settled more than thirty minutes ago is not taken")]
    public async Task WhatSettledMoreThanThirtyMinutesAgoIsNotTaken()
    {
        DateTime now = await ADayOfItsOwnAsync(11);
        RecordingId longAgo = await ledgers.RecordingBeganAsync(now.AddMinutes(-60));
        await ledgers.RecordingEndedAsync(longAgo, RecordingOutcome.Complete, now.AddMinutes(-31));
        RecordingId lately = await ledgers.RecordingBeganAsync(now.AddMinutes(-120));
        await ledgers.RecordingEndedAsync(lately, RecordingOutcome.Complete, now.AddMinutes(-29));

        await ledgers.LookAsync(now);

        Assert.Empty(await ledgers.NoticesOfAsync(longAgo.Value));
        Assert.Equal([NoticeKind.RecordingCompleted], await ledgers.KindsOfAsync(lately.Value));
    }

    [Fact(DisplayName = "BR-NC-004: what happened exactly thirty minutes ago is taken, and what happened a moment before that is not")]
    public async Task WhatHappenedExactlyThirtyMinutesAgoIsTakenAndAMomentBeforeIsNot()
    {
        DateTime now = await ADayOfItsOwnAsync(24);
        await ledgers.EmptyTheEncodesAsync();
        DateTime edge = now.AddMinutes(-30);
        DateTime pastTheEdge = edge - TimeSpan.FromMicroseconds(1);
        RecordingId endedOnTheEdge = await ledgers.RecordingBeganAsync(now.AddMinutes(-120));
        await ledgers.RecordingEndedAsync(endedOnTheEdge, RecordingOutcome.Complete, edge);
        RecordingId endedPastTheEdge = await ledgers.RecordingBeganAsync(now.AddMinutes(-120));
        await ledgers.RecordingEndedAsync(endedPastTheEdge, RecordingOutcome.Complete, pastTheEdge);
        ReservationId missedOnTheEdge = ReservationId.New();
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, edge, missedOnTheEdge);
        ReservationId missedPastTheEdge = ReservationId.New();
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, pastTheEdge, missedPastTheEdge);
        EncodeJobId failedPastTheEdge = await ledgers.EncodeIsQueuedAsync(endedPastTheEdge, now.AddMinutes(-110));
        await ledgers.EncodeStartedAsync(failedPastTheEdge, now.AddMinutes(-100));
        await ledgers.EncodeFailedAsync(failedPastTheEdge, pastTheEdge);
        EncodeJobId failedOnTheEdge = await ledgers.EncodeIsQueuedAsync(endedOnTheEdge, now.AddMinutes(-110));
        await ledgers.EncodeStartedAsync(failedOnTheEdge, now.AddMinutes(-100));
        await ledgers.EncodeFailedAsync(failedOnTheEdge, edge);
        QualityIncidentId fellOnTheEdge = await ledgers.ReceptionFellAsync(edge);
        QualityIncidentId fellPastTheEdge = await ledgers.ReceptionFellAsync(pastTheEdge);

        await ledgers.LookAsync(now);
        await ledgers.ReceptionCameBackAsync(fellOnTheEdge, now);
        await ledgers.ReceptionCameBackAsync(fellPastTheEdge, now);

        Assert.Equal([NoticeKind.RecordingCompleted], await ledgers.KindsOfAsync(endedOnTheEdge.Value));
        Assert.Equal([NoticeKind.ReservationMissed], await ledgers.KindsOfAsync(missedOnTheEdge.Value));
        Assert.Equal([NoticeKind.EncodeFailed], await ledgers.KindsOfAsync(failedOnTheEdge.Value));
        Assert.Equal([NoticeKind.QualityIncidentDetected], await ledgers.KindsOfAsync(fellOnTheEdge.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(endedPastTheEdge.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(missedPastTheEdge.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(failedPastTheEdge.Value));
        Assert.Empty(await ledgers.NoticesOfAsync(fellPastTheEdge.Value));
    }

    [Fact(DisplayName = "BR-NC-004: what a row dates after this moment is not taken until that moment has come")]
    public async Task WhatARowDatesAfterThisMomentWaitsUntilThatMomentHasCome()
    {
        DateTime now = await ADayOfItsOwnAsync(29);
        await ledgers.EmptyTheEncodesAsync();
        DateTime ahead = now.AddMinutes(10);
        RecordingId endedNow = await ledgers.RecordingBeganAsync(now.AddMinutes(-120));
        await ledgers.RecordingEndedAsync(endedNow, RecordingOutcome.Complete, now);
        RecordingId endedAhead = await ledgers.RecordingBeganAsync(now.AddMinutes(-120));
        await ledgers.RecordingEndedAsync(endedAhead, RecordingOutcome.Complete, ahead);
        ReservationId missedAhead = ReservationId.New();
        await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, ahead, missedAhead);
        EncodeJobId failedAhead = await ledgers.EncodeIsQueuedAsync(endedAhead, now.AddMinutes(-110));
        await ledgers.EncodeStartedAsync(failedAhead, now.AddMinutes(-100));
        await ledgers.EncodeFailedAsync(failedAhead, ahead);
        QualityIncidentId fellAhead = await ledgers.ReceptionFellAsync(ahead);

        await ledgers.LookAsync(now);
        string[] endedNowAtFirst = [.. (await ledgers.KindsOfAsync(endedNow.Value)).Select(kind => kind.ToString())];
        int aheadAtFirst = (await ledgers.NoticesOfAsync(endedAhead.Value)).Count
            + (await ledgers.NoticesOfAsync(missedAhead.Value)).Count
            + (await ledgers.NoticesOfAsync(failedAhead.Value)).Count
            + (await ledgers.NoticesOfAsync(fellAhead.Value)).Count;
        await ledgers.LookAsync(ahead);
        await ledgers.ReceptionCameBackAsync(fellAhead, ahead.AddMinutes(1));

        Assert.Equal([nameof(NoticeKind.RecordingCompleted)], endedNowAtFirst);
        Assert.Equal(0, aheadAtFirst);
        Assert.Equal([NoticeKind.RecordingCompleted], await ledgers.KindsOfAsync(endedAhead.Value));
        Assert.Equal([NoticeKind.ReservationMissed], await ledgers.KindsOfAsync(missedAhead.Value));
        Assert.Equal([NoticeKind.EncodeFailed], await ledgers.KindsOfAsync(failedAhead.Value));
        Assert.Equal([NoticeKind.QualityIncidentDetected], await ledgers.KindsOfAsync(fellAhead.Value));
    }

    [Fact(DisplayName = "A recording that began and ended between two looks is told of twice in the one look")]
    public async Task ARecordingThatBeganAndEndedBetweenTwoLooksIsToldOfTwiceInTheOneLook()
    {
        DateTime now = await ADayOfItsOwnAsync(12);
        await ledgers.LookAsync(now);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddSeconds(2));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Truncated, now.AddSeconds(10));

        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal(
            [NoticeKind.RecordingStarted, NoticeKind.RecordingTruncated],
            await ledgers.KindsOfAsync(recording.Value));
    }

    [Fact(DisplayName = "A row written late, timed before notices already taken, is taken at the next look")]
    public async Task ARowWrittenLateTimedBeforeNoticesAlreadyTakenIsTakenAtTheNextLook()
    {
        DateTime now = await ADayOfItsOwnAsync(13);
        RecordingId encoded = await ledgers.RecordingBeganAsync(now.AddMinutes(-90));
        EncodeJobId job = await ledgers.EncodeIsQueuedAsync(encoded, now.AddMinutes(-40));
        RecordingId later = await ledgers.RecordingBeganAsync(now.AddMinutes(-1));

        await ledgers.LookAsync(now);
        await ledgers.EncodeStartedAsync(job, now.AddMinutes(-5));
        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal([NoticeKind.RecordingStarted], await ledgers.KindsOfAsync(later.Value));
        Notice started = Assert.Single(await ledgers.NoticesOfAsync(job.Value));
        Assert.Equal(NoticeKind.EncodeStarted, started.Kind);
        Assert.Equal(now.AddMinutes(-5), started.OccurredAt);
        Assert.Equal(now + BetweenLooks, started.TakenAt);
    }

    [Fact(DisplayName = "BR-ND-007: the words of a notice are kept with it: the programme, the station, Japan's time and the tone")]
    public async Task TheWordsOfANoticeAreKeptWithIt()
    {
        DateTime now = await ADayOfItsOwnAsync(14);
        await ledgers.StationIsNamedAsync(now);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-10));

        await ledgers.LookAsync(now);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(recording.Value));
        Assert.Equal("録画を始めました", notice.Title);
        Assert.Equal(NoticeTone.Informative, notice.Tone);
        Assert.StartsWith(
            $"{NoticeLedgers.ProgrammeName}\n{NoticeLedgers.StationName} · 03/15(土) 20:50〜21:20",
            notice.Body,
            StringComparison.Ordinal);
        Assert.Equal(now.AddMinutes(-10), notice.OccurredAt);
        Assert.Null(notice.Link);
    }

    [Fact(DisplayName = "A notice links to the screen only when the address the screens are reached at is known")]
    public async Task ANoticeLinksToTheScreenOnlyWhenTheAddressTheScreensAreReachedAtIsKnown()
    {
        DateTime now = await ADayOfItsOwnAsync(15);
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-10));

        await ledgers.LookAsync(now, NoticeLinkBase.At(new Uri("https://screens.example")));

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(recording.Value));
        Assert.Equal($"https://screens.example/recordings/{recording.Value:N}", notice.Link);
    }

    [Fact(DisplayName = "A reservation that lost its tuner is told of with the programmes recorded instead, and a moved programme is told of")]
    public async Task AReservationThatLostItsTunerIsToldOfWithTheProgrammesRecordedInstead()
    {
        DateTime now = await ADayOfItsOwnAsync(16);
        ReservationId winner = await ledgers.ReservationIsPlannedAsync("Another made-up programme", now.AddHours(3));
        ReservationOutcomeId competing = await ledgers.OutcomeAsync(
            ReservationOutcomeKind.Competing,
            now.AddMinutes(-3),
            recordedInstead: [winner.Value]);
        ReservationOutcomeId moved = await ledgers.OutcomeAsync(ReservationOutcomeKind.ProgrammeMoved, now.AddMinutes(-3));

        await ledgers.LookAsync(now);

        Notice lost = Assert.Single(await ledgers.NoticesOfAsync(competing.Value));
        Assert.Equal(NoticeKind.ReservationCompeting, lost.Kind);
        Assert.Contains("Another made-up programme", lost.Body, StringComparison.Ordinal);
        Assert.Equal([NoticeKind.ProgrammeMoved], await ledgers.KindsOfAsync(moved.Value));
    }

    [Fact(DisplayName = "An encode is told of when it starts and when it fails, by the programme and the format")]
    public async Task AnEncodeIsToldOfWhenItStartsAndWhenItFails()
    {
        DateTime now = await ADayOfItsOwnAsync(17);
        await ledgers.EmptyTheEncodesAsync();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-90));
        EncodeJobId job = await ledgers.EncodeIsQueuedAsync(recording, now.AddMinutes(-40));
        await ledgers.EncodeStartedAsync(job, now.AddMinutes(-4));
        await ledgers.EncodeFailedAsync(job, now.AddMinutes(-2));

        await ledgers.LookAsync(now);

        IReadOnlyList<Notice> notices = await ledgers.NoticesOfAsync(job.Value);
        Assert.Equal([NoticeKind.EncodeStarted, NoticeKind.EncodeFailed], notices.Select(notice => notice.Kind));
        Assert.All(notices, notice => Assert.StartsWith(NoticeLedgers.ProgrammeName, notice.Body, StringComparison.Ordinal));
        Assert.Contains("H.264", notices[0].Body, StringComparison.Ordinal);
        Assert.Equal(NoticeTone.Failure, notices[1].Tone);
    }

    [Fact(DisplayName = "An encode that completes is told of with how far the artefact shrank from the recording")]
    public async Task AnEncodeThatCompletesIsToldOfWithHowFarItShrank()
    {
        DateTime now = await ADayOfItsOwnAsync(27);
        await ledgers.EmptyTheEncodesAsync();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-90));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Complete, now.AddMinutes(-60));
        EncodeJobId job = await ledgers.EncodeIsQueuedAsync(recording, now.AddMinutes(-40));
        await ledgers.EncodeStartedAsync(job, now.AddMinutes(-4));
        await ledgers.EncodeCompletedAsync(job, now.AddMinutes(-2));

        await ledgers.LookAsync(now, artefactBytes: 300_000);

        IReadOnlyList<Notice> notices = await ledgers.NoticesOfAsync(job.Value);
        Assert.Equal([NoticeKind.EncodeStarted, NoticeKind.EncodeCompleted], notices.Select(notice => notice.Kind));
        Assert.DoesNotContain("大きさ", notices[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("大きさ: 1 MB → 293 KB(元の 25%)", notices[1].Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "An encode that completes is told of without a size when its artefact cannot be weighed")]
    public async Task AnEncodeWhoseArtefactCannotBeWeighedIsToldOfWithoutASize()
    {
        DateTime now = await ADayOfItsOwnAsync(28);
        await ledgers.EmptyTheEncodesAsync();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-90));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Complete, now.AddMinutes(-60));
        EncodeJobId job = await ledgers.EncodeIsQueuedAsync(recording, now.AddMinutes(-40));
        await ledgers.EncodeStartedAsync(job, now.AddMinutes(-4));
        await ledgers.EncodeCompletedAsync(job, now.AddMinutes(-2));

        await ledgers.LookAsync(now);

        Notice completed = (await ledgers.NoticesOfAsync(job.Value))[^1];
        Assert.Equal(NoticeKind.EncodeCompleted, completed.Kind);
        Assert.DoesNotContain("大きさ", completed.Body, StringComparison.Ordinal);
    }

    private static Notice ANotice(Guid source, DateTime at)
        => Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingStarted,
            source,
            at,
            new NoticeWords("A title", "A body", null),
            at);

    private static NotificationDelivery ADelivery(Notice notice, NotificationDestinationId destination, DateTime at)
        => NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, at);

    private async Task<DateTime> ADayOfItsOwnAsync(int day)
    {
        await ledgers.EmptyTheNoticesAsync();

        return Epoch.AddDays(day);
    }
}
