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
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20));
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Complete, now.AddMinutes(-1));

        NotificationIntake first = await ledgers.LookAsync(now);
        NotificationIntake second = await ledgers.LookAsync(now + BetweenLooks);

        Assert.True(first.Taken >= 2);
        Assert.True(first.Queued >= 2);
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

    [Fact(DisplayName = "BR-ND-005: a failed recording that has a reservation is told of once, from the reservation's outcome")]
    public async Task AFailedRecordingThatHasAReservationIsToldOfOnceFromTheReservationsOutcome()
    {
        DateTime now = await ADayOfItsOwnAsync(4);
        ReservationId reservation = ReservationId.New();
        RecordingId recording = await ledgers.RecordingBeganAsync(now.AddMinutes(-20), reservation);
        await ledgers.RecordingEndedAsync(recording, RecordingOutcome.Failed, now.AddMinutes(-5));
        ReservationOutcomeId outcome = await ledgers.OutcomeAsync(
            ReservationOutcomeKind.RecordingFailure,
            now.AddMinutes(-5),
            reservation,
            recordingOutcome: RecordingOutcome.Failed,
            faults: [RecordingFault.ShortOfTheWindow]);

        await ledgers.LookAsync(now);
        await ledgers.LookAsync(now + BetweenLooks);

        Assert.Equal([NoticeKind.RecordingStarted], await ledgers.KindsOfAsync(recording.Value));
        Assert.Equal([NoticeKind.ReservationMissed], await ledgers.KindsOfAsync(outcome.Value));
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
        ReservationOutcomeId missed = await ledgers.OutcomeAsync(ReservationOutcomeKind.Missed, now.AddMinutes(-3));

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
