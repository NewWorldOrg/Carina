using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Notifications;
using Carina.Infrastructure.Tests.Reservations;
using Carina.Infrastructure.Tests.Thumbnails;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using WatchedProgrammes = Carina.Infrastructure.Tests.Notifications.WatchedProgrammes;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWatchRoundTests(RepositoryDatabase database)
{
    private const int Lead = 3;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Asked = new(2031, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Starts = Asked.AddHours(1);

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(15);

    private readonly NoticeLedgers ledgers = new(database);

    private readonly WatchedProgrammes watched = new(database);

    [Fact(DisplayName = "BR-NS-003: a programme that waits is left alone until its minutes before the start are reached")]
    public async Task AProgrammeThatWaitsIsLeftAloneUntilItsMinutesBeforeTheStartAreReached()
    {
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();

        NotificationIntake look = await LookAsync(Starts.AddMinutes(-Lead).AddSeconds(-1));

        Assert.Equal(0, look.Taken);
        Assert.Equal(ProgrammeWatchState.Waiting, (await watched.RowAsync(watch)).State);
        Assert.Empty(await ledgers.NoticesOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-NS-003: once its minutes before the start are reached, a programme that waits is told of to its destination and the time is kept")]
    public async Task OnceItsMinutesBeforeTheStartAreReachedAProgrammeThatWaitsIsToldOf()
    {
        (ProgrammeWatchId watch, NotificationDestinationId destination) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(-Lead);

        NotificationIntake look = await LookAsync(now);

        ProgrammeWatch row = await watched.RowAsync(watch);
        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(watch.Value));
        NotificationDelivery delivery = Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting));
        Assert.Equal(new NotificationIntake(1, 1, 1), look);
        Assert.Equal(ProgrammeWatchState.Notified, row.State);
        Assert.Equal(now, row.NotifiedAt);
        Assert.Equal(NoticeKind.ProgrammeStarting, notice.Kind);
        Assert.Equal(now, notice.OccurredAt);
        Assert.Equal(destination, delivery.DestinationId);
        Assert.Equal(NotificationDeliveryState.Queued, delivery.State);
    }

    [Fact(DisplayName = "BR-NS-003: a programme asked to be told of at its start waits through the second before it and is told of as begun at the start")]
    public async Task AProgrammeAskedToBeToldOfAtItsStartIsToldOfAsBegunAtTheStart()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(await watched.AnnouncedAsync(Starts, Asked), Asked, 0, destination);

        NotificationIntake before = await LookAsync(Starts.AddSeconds(-1));
        ProgrammeWatch waiting = await watched.RowAsync(watch);
        IReadOnlyList<Notice> nothing = await ledgers.NoticesOfAsync(watch.Value);
        NotificationIntake atTheStart = await LookAsync(Starts);

        ProgrammeWatch row = await watched.RowAsync(watch);
        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(watch.Value));
        Assert.Equal(new NotificationIntake(1, 0, 0), before);
        Assert.Equal(ProgrammeWatchState.Waiting, waiting.State);
        Assert.Empty(nothing);
        Assert.Equal(new NotificationIntake(1, 1, 1), atTheStart);
        Assert.Equal(ProgrammeWatchState.Notified, row.State);
        Assert.Equal(Starts, row.NotifiedAt);
        Assert.Equal(NoticeKind.ProgrammeStarting, notice.Kind);
        Assert.Equal(Starts, notice.OccurredAt);
        Assert.Equal(WordsOf(row, Starts), (notice.Title, notice.Body));
        Assert.NotEqual(WordsOf(row, Starts.AddSeconds(-1)).Title, notice.Title);
        Assert.NotEqual(WordsOf(row, Starts.AddMinutes(1)).Title, notice.Title);
        Assert.Equal(destination, Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a look that comes back after the start, with the programme on the air, tells of it as on the air")]
    public async Task ALookThatComesBackAfterTheStartTellsOfTheProgrammeAsOnTheAir()
    {
        (ProgrammeWatchId watch, NotificationDestinationId destination) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(10);

        await LookAsync(now);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(watch.Value));
        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(watch)).State);
        Assert.Equal(NoticeKind.ProgrammeStarting, notice.Kind);
        Assert.Equal(now, notice.OccurredAt);
        Assert.Equal(destination, Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a look that comes back after the broadcast ended passes the programme over and sends nothing")]
    public async Task ALookThatComesBackAfterTheBroadcastEndedPassesTheProgrammeOver()
    {
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();

        NotificationIntake look = await LookAsync(Starts + WatchedProgrammes.Length);

        ProgrammeWatch row = await watched.RowAsync(watch);
        Assert.Equal(0, look.Taken);
        Assert.Equal(ProgrammeWatchState.Skipped, row.State);
        Assert.Null(row.NotifiedAt);
        Assert.Empty(await ledgers.NoticesOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-NS-003: a programme the guide stops naming before it begins is cancelled, and that is told once")]
    public async Task AProgrammeTheGuideStopsNamingBeforeItBeginsIsCancelledAndThatIsToldOnce()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, destination);
        await watched.DroppedAsync(programme, Asked.AddMinutes(20));

        await LookAsync(Asked.AddMinutes(21));
        NotificationIntake again = await LookAsync(Asked.AddMinutes(21) + BetweenLooks);

        Notice notice = Assert.Single(await ledgers.NoticesOfAsync(watch.Value));
        Assert.Equal(0, again.Seen);
        Assert.Equal(ProgrammeWatchState.Cancelled, (await watched.RowAsync(watch)).State);
        Assert.Equal(NoticeKind.WatchedProgrammeGone, notice.Kind);
        Assert.Equal(destination, Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.WatchedProgrammeGone)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a programme that moves before it is told of is told of by its new time")]
    public async Task AProgrammeThatMovesBeforeItIsToldOfIsToldOfByItsNewTime()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, destination);
        DateTime later = Starts.AddMinutes(30);
        await watched.MovedAsync(programme, later, Asked.AddMinutes(20));

        NotificationIntake atTheOldTime = await LookAsync(Starts.AddMinutes(-Lead));
        ProgrammeWatch followed = await watched.RowAsync(watch);
        NotificationIntake atTheNewTime = await LookAsync(later.AddMinutes(-Lead));

        Assert.Equal(0, atTheOldTime.Taken);
        Assert.Equal(ProgrammeWatchState.Waiting, followed.State);
        Assert.Equal(later, followed.StartsAt);
        Assert.Equal(later + WatchedProgrammes.Length, followed.EndsAt);
        Assert.Equal(1, atTheNewTime.Taken);
        Assert.Equal([NoticeKind.ProgrammeStarting], await ledgers.KindsOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-NS-003: a programme that moves after it was told of is told once more, and stays told of")]
    public async Task AProgrammeThatMovesAfterItWasToldOfIsToldOnceMoreAndStaysToldOf()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, destination);
        DateTime told = Starts.AddMinutes(-Lead);
        await LookAsync(told);
        DateTime later = Starts.AddMinutes(30);
        await watched.MovedAsync(programme, later, told.AddSeconds(5));

        NotificationIntake first = await LookAsync(told + BetweenLooks);
        NotificationIntake second = await LookAsync(told + BetweenLooks + BetweenLooks);

        ProgrammeWatch row = await watched.RowAsync(watch);
        Assert.Equal(1, first.Taken);
        Assert.Equal(0, second.Taken);
        Assert.Equal(ProgrammeWatchState.Notified, row.State);
        Assert.Equal(told, row.NotifiedAt);
        Assert.Equal(later, row.StartsAt);
        Assert.Equal(
            [NoticeKind.ProgrammeStarting, NoticeKind.WatchedProgrammeMoved],
            await ledgers.KindsOfAsync(watch.Value));
        Assert.Equal(destination, Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.WatchedProgrammeMoved)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a programme the guide stops naming after it was told of, before it begins, is told once more and stays told of")]
    public async Task AProgrammeTheGuideStopsNamingAfterItWasToldOfIsToldOnceMoreAndStaysToldOf()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, destination);
        DateTime told = Starts.AddMinutes(-Lead);
        await LookAsync(told);
        await watched.DroppedAsync(programme, told.AddSeconds(5));

        await LookAsync(told + BetweenLooks);
        NotificationIntake again = await LookAsync(told + BetweenLooks + BetweenLooks);

        Assert.Equal(0, again.Taken);
        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(watch)).State);
        Assert.Equal(
            [NoticeKind.ProgrammeStarting, NoticeKind.WatchedProgrammeGone],
            await ledgers.KindsOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-ND-004: a programme told of at one look is not told of again at the next")]
    public async Task AProgrammeToldOfAtOneLookIsNotToldOfAgainAtTheNext()
    {
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(-Lead);
        await LookAsync(now);

        NotificationIntake again = await LookAsync(now + BetweenLooks);

        Assert.Equal(new NotificationIntake(1, 0, 0), again);
        Assert.Single(await ledgers.NoticesOfAsync(watch.Value));
        Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting));
    }

    [Fact(DisplayName = "BR-NC-003: a programme asked to be told of goes to the destinations asked for with it, whatever the rule sets name")]
    public async Task AProgrammeAskedToBeToldOfGoesToTheDestinationsAskedForWithIt()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId asked = await ledgers.DestinationAsync("Asked for", Asked);
        NotificationDestinationId switchedOff = await ledgers.DestinationAsync("Switched off", Asked, enabled: false);
        NotificationDestinationId ofARule = await ledgers.DestinationAsync("Named by a rule set", Asked);
        await ledgers.RuleAsync("Everything a rule set may name", Asked, [.. NoticeKinds.ChosenByRules], [ofARule]);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, asked, switchedOff);

        NotificationIntake look = await LookAsync(Starts.AddMinutes(-Lead));

        Assert.Equal(1, look.Queued);
        Assert.Equal(asked, Assert.Single(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a programme whose destinations are all switched off by the time it is due is told of with nothing to send")]
    public async Task AProgrammeWhoseDestinationsAreAllSwitchedOffByTheTimeItIsDueIsToldOfWithNothingToSend()
    {
        (ProgrammeWatchId watch, NotificationDestinationId destination) = await AskedOfADestinationAsync();
        await ledgers.SwitchedOffAsync(destination, Asked.AddMinutes(10));
        DateTime now = Starts.AddMinutes(-Lead);

        NotificationIntake look = await LookAsync(now);

        ProgrammeWatch row = await watched.RowAsync(watch);
        Assert.Equal(new NotificationIntake(1, 1, 0), look);
        Assert.Equal(ProgrammeWatchState.Notified, row.State);
        Assert.Equal(now, row.NotifiedAt);
        Assert.Equal([destination], row.Destinations);
        Assert.Equal([NoticeKind.ProgrammeStarting], await ledgers.KindsOfAsync(watch.Value));
        Assert.Empty(await ledgers.DeliveriesOfAsync(watch.Value, NoticeKind.ProgrammeStarting));
    }

    [Fact(DisplayName = "BR-NS-003: when the notice cannot be written the programme still waits, and the next look tells of it")]
    public async Task WhenTheNoticeCannotBeWrittenTheProgrammeStillWaitsAndTheNextLookTellsOfIt()
    {
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(-Lead);

        NotificationIntake failed = await LookAsync(now, held => new NoticesThatCannotBeWritten(held));

        Assert.Equal(0, failed.Taken);
        Assert.Equal(ProgrammeWatchState.Waiting, (await watched.RowAsync(watch)).State);
        Assert.Empty(await ledgers.NoticesOfAsync(watch.Value));

        NotificationIntake next = await LookAsync(now + BetweenLooks);

        Assert.Equal(1, next.Taken);
        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(watch)).State);
    }

    [Fact(DisplayName = "BR-NS-003: when one programme's notice cannot be written the other programmes due at the same look are told of, and one error is logged")]
    public async Task WhenOneProgrammesNoticeCannotBeWrittenTheOthersDueAtTheSameLookAreToldOf()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeWatchId refused = await watched.AskedAsync(await watched.AnnouncedAsync(Starts, Asked), Asked, Lead, destination);
        ProgrammeWatchId written = await watched.AskedAsync(
            await watched.AnnouncedAsync(Starts.AddMinutes(1), Asked),
            Asked,
            Lead,
            destination);
        HeardOf<ProgrammeWatchRound> heard = new();

        NotificationIntake look = await LookAsync(
            Starts.AddMinutes(1 - Lead),
            held => new NoticesThatCannotBeWritten(held, refused.Value),
            heard);

        string error = Assert.Single(heard.Said, line => line.StartsWith("Error ", StringComparison.Ordinal));
        Assert.Equal(new NotificationIntake(2, 1, 1), look);
        Assert.Contains(refused.Value.ToString(), error, StringComparison.Ordinal);
        Assert.Equal(ProgrammeWatchState.Waiting, (await watched.RowAsync(refused)).State);
        Assert.Empty(await ledgers.NoticesOfAsync(refused.Value));
        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(written)).State);
        Assert.Equal(destination, Assert.Single(await ledgers.DeliveriesOfAsync(written.Value, NoticeKind.ProgrammeStarting)).DestinationId);
    }

    [Fact(DisplayName = "BR-NS-003: a programme withdrawn after the look listed it and before the look wrote is not told of")]
    public async Task AProgrammeWithdrawnAfterTheLookListedItAndBeforeTheLookWroteIsNotToldOf()
    {
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(-Lead);

        NotificationIntake look = await LookAsync(
            now,
            held => new NoticesReadAfter(held, () => watched.WithdrawnAsync(watch, now)));

        Assert.Equal(new NotificationIntake(1, 0, 0), look);
        Assert.Equal(ProgrammeWatchState.Cancelled, (await watched.RowAsync(watch)).State);
        Assert.Empty(await ledgers.NoticesOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-ND-013: a programme whose only destination is removed after the look listed it and before the look wrote is not told of")]
    public async Task AProgrammeWhoseOnlyDestinationIsRemovedAfterTheLookListedItIsNotToldOf()
    {
        (ProgrammeWatchId watch, NotificationDestinationId destination) = await AskedOfADestinationAsync();
        DateTime now = Starts.AddMinutes(-Lead);

        NotificationIntake look = await LookAsync(
            now,
            held => new NoticesReadAfter(held, () => watched.DestinationRemovedAsync(destination, now)));

        ProgrammeWatch row = await watched.RowAsync(watch);
        Assert.Equal(new NotificationIntake(1, 0, 0), look);
        Assert.Equal(ProgrammeWatchState.Cancelled, row.State);
        Assert.Empty(row.Destinations);
        Assert.Empty(await ledgers.NoticesOfAsync(watch.Value));
    }

    [Fact(DisplayName = "BR-ND-009: telling of a programme writes no reservation")]
    public async Task TellingOfAProgrammeWritesNoReservation()
    {
        int before = await ReservationsAsync();
        (ProgrammeWatchId watch, _) = await AskedOfADestinationAsync();

        await LookAsync(Starts.AddMinutes(-Lead));

        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(watch)).State);
        Assert.Equal(before, await ReservationsAsync());
    }

    [Fact(DisplayName = "BR-ND-009: a programme reserved for recording can be asked to be told of as well, and telling of it leaves the reservation as it was")]
    public async Task AProgrammeReservedForRecordingCanBeAskedToBeToldOfAsWell()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        Reservation reserved = await ReservedAsync(programme);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, Lead, destination);

        NotificationIntake look = await LookAsync(Starts.AddMinutes(-Lead));

        Reservation kept = await ReservationAsync(reserved.Id);
        Assert.Equal(new NotificationIntake(1, 1, 1), look);
        Assert.Equal(ProgrammeWatchState.Notified, (await watched.RowAsync(watch)).State);
        Assert.Equal(programme, (await watched.RowAsync(watch)).Programme);
        Assert.Equal(
            (reserved.State, reserved.Programme, reserved.StartAt, reserved.EndAt),
            (kept.State, kept.Programme, kept.StartAt, kept.EndAt));
    }

    private async Task<NotificationIntake> LookAsync(
        DateTime now,
        Func<INoticeRepository, INoticeRepository>? notices = null,
        ILogger<ProgrammeWatchRound>? logger = null)
    {
        await using ServiceProvider services = database.Infrastructure(replace =>
        {
            replace.AddSingleton<TimeProvider>(new HandTurnedClock(new DateTimeOffset(now)));

            if (logger is not null)
            {
                replace.AddSingleton(logger);
            }

            if (notices is not null)
            {
                ServiceDescriptor held = replace.Last(descriptor => descriptor.ServiceType == typeof(INoticeRepository));
                Type implementation = held.ImplementationType
                    ?? throw new InvalidOperationException("The notices are not registered by their class, so they cannot be wrapped.");
                replace.Add(ServiceDescriptor.Describe(
                    typeof(INoticeRepository),
                    provider => notices((INoticeRepository)ActivatorUtilities.CreateInstance(provider, implementation)),
                    held.Lifetime));
            }
        });
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ProgrammeWatchRound>().TakeAsync(Cancel);
    }

    private async Task<Reservation> ReservedAsync(ProgrammeId programme)
    {
        Reservation reservation = ReservationFixtures.Planned(
            programme: new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, Starts),
            endAt: Starts + WatchedProgrammes.Length,
            snapshot: ReservationFixtures.Snapshot(WatchedProgrammes.ProgrammeName));

        await using ServiceProvider services = database.Infrastructure();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IReservationRepository>().AddAsync(reservation, Cancel);

        return reservation;
    }

    private async Task<Reservation> ReservationAsync(ReservationId id)
    {
        await using ServiceProvider services = database.Infrastructure();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return Assert.IsType<Reservation>(await scope.ServiceProvider.GetRequiredService<IReservationRepository>().FindAsync(id, Cancel));
    }

    private async Task<(ProgrammeWatchId Watch, NotificationDestinationId Destination)> AskedOfADestinationAsync()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Asked);
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);

        return (await watched.AskedAsync(programme, Asked, Lead, destination), destination);
    }

    private static (string Title, string Body) WordsOf(ProgrammeWatch watch, DateTime toldAt)
    {
        NoticeWords words = NoticeWording.Of(
            ProgrammeWatchNoticeSource.Of(watch, NoticeKind.ProgrammeStarting, toldAt).Happening(),
            NoticeLinkBase.None);

        return (words.Title, words.Body);
    }

    private async Task<int> ReservationsAsync()
    {
        await using CarinaDbContext context = database.Open();

        return await context.Set<Reservation>().CountAsync(Cancel);
    }

    private sealed class NoticesThatCannotBeWritten(INoticeRepository held, Guid? onlyOf = null) : INoticeRepository
    {
        public Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken) => held.FindAsync(id, cancellationToken);

        public Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
            => held.HeldAmongAsync(sourceIds, cancellationToken);

        public Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken)
            => onlyOf is null || notice.SourceId == onlyOf
                ? throw new InvalidOperationException("The ledger could not be written.")
                : held.AddAsync(notice, deliveries, cancellationToken);

        public Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken)
            => held.RemoveBeforeAsync(before, cancellationToken);
    }

    private sealed class NoticesReadAfter(INoticeRepository held, Func<Task> happensFirst) : INoticeRepository
    {
        private bool happened;

        public Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken) => held.FindAsync(id, cancellationToken);

        public async Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
        {
            if (!happened)
            {
                happened = true;
                await happensFirst();
            }

            return await held.HeldAmongAsync(sourceIds, cancellationToken);
        }

        public Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken)
            => held.AddAsync(notice, deliveries, cancellationToken);

        public Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken)
            => held.RemoveBeforeAsync(before, cancellationToken);
    }
}
