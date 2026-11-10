using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationWritesTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string Address = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    private const int AtOnce = 6;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Noon = new(2031, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly NotificationDeliverySettings Settings = new();

    private readonly HandTurnedClock clock = new(new DateTimeOffset(Noon));

    private readonly WebhookUrlSeal seal = new(new EphemeralDataProtectionProvider());

    [Fact(DisplayName = "BR-NT-006: of the sets of rules defined at the same moment beside nineteen, one is kept and the rest are refused")]
    public async Task OfTheSetsOfRulesDefinedAtTheSameMomentBesideNineteenOneIsKept()
    {
        await EmptyAsync();
        NotificationDestinationId destination = await RegisteredAsync("Somewhere");
        await RulesAreHeldAsync(destination, NotificationRule.MostRules - 2);

        ServiceResult<NotificationRule, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(_ => AsOneCallerAsync(caller => caller.Rules.DefineAsync(Asked(destination), Cancel))));

        Assert.Single(answers, answer => answer.IsSuccess);
        Assert.All(
            answers.Where(answer => !answer.IsSuccess),
            answer => Assert.Equal(NotificationFailure.TooManyRules, answer.ErrorType));
        Assert.Equal(NotificationRule.MostRules, (await HeldRulesAsync()).Count);
    }

    [Fact(DisplayName = "D-9: destinations registered at the same moment into an empty ledger make one set of rules between them")]
    public async Task DestinationsRegisteredAtTheSameMomentMakeOneSetOfRulesBetweenThem()
    {
        await EmptyAsync();

        ServiceResult<ListedNotificationDestination, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(_ => AsOneCallerAsync(caller => caller.Destinations.RegisterAsync("Somewhere", Address, Cancel))));

        NotificationRule made = Assert.Single(await HeldRulesAsync());

        Assert.All(answers, answer => Assert.True(answer.IsSuccess));
        Assert.Equal(AtOnce, (await HeldDestinationsAsync()).Count);
        Assert.Contains(Assert.Single(made.Destinations).Value, answers.Select(answer => answer.Data!.Destination.Id.Value));
    }

    [Fact(DisplayName = "BR-NT-006: a set of rules is not defined while another writer of the notifications holds its turn")]
    public async Task ASetOfRulesIsNotDefinedWhileAnotherWriterHoldsItsTurn()
    {
        await EmptyAsync();
        NotificationDestinationId destination = await RegisteredAsync("Somewhere");

        await WaitsItsTurnAsync(() => AsOneCallerAsync(caller => caller.Rules.DefineAsync(Asked(destination), Cancel)));

        Assert.Equal(2, (await HeldRulesAsync()).Count);
    }

    [Fact(DisplayName = "D-9: a destination is not registered while another writer of the notifications holds its turn")]
    public async Task ADestinationIsNotRegisteredWhileAnotherWriterHoldsItsTurn()
    {
        await EmptyAsync();

        await WaitsItsTurnAsync(() => AsOneCallerAsync(caller => caller.Destinations.RegisterAsync("Somewhere", Address, Cancel)));

        Assert.Single(await HeldDestinationsAsync());
    }

    [Fact(DisplayName = "BR-NT-004: a destination is not removed while another writer of the notifications holds its turn")]
    public async Task ADestinationIsNotRemovedWhileAnotherWriterHoldsItsTurn()
    {
        await EmptyAsync();
        NotificationDestinationId destination = await RegisteredAsync("Somewhere");

        await WaitsItsTurnAsync(() => AsOneCallerAsync(caller => caller.Destinations.RemoveAsync(destination, Cancel)));

        Assert.Empty(await HeldDestinationsAsync());
    }

    [Fact(DisplayName = "BR-NS-001: removing a destination takes the deliveries to it with it, and leaves the notices they carried")]
    public async Task RemovingADestinationTakesTheDeliveriesToItWithIt()
    {
        await EmptyAsync();
        NotificationDestinationId leaving = await RegisteredAsync("Leaving");
        NotificationDestinationId staying = await RegisteredAsync("Staying");
        Notice notice = await ToldAsync(leaving, staying);

        ServiceResult<NotificationRemoved, NotificationFailure> answer =
            await AsOneCallerAsync(caller => caller.Destinations.RemoveAsync(leaving, Cancel));

        await using CarinaDbContext context = Context();
        IReadOnlyList<NotificationDelivery> left = await new NotificationDeliveryRepository(context).ListOfAsync(notice.Id, Cancel);

        Assert.True(answer.IsSuccess);
        Assert.Equal(staying, Assert.Single(left).DestinationId);
        Assert.NotNull(await new NoticeRepository(context).FindAsync(notice.Id, Cancel));
    }

    [Fact(DisplayName = "BR-NT-004: removing a destination takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingADestinationTakesItOutOfTheSetsOfRules()
    {
        await EmptyAsync();
        NotificationDestinationId leaving = await RegisteredAsync("Leaving");
        NotificationDestinationId staying = await RegisteredAsync("Staying");
        ServiceResult<NotificationRule, NotificationFailure> shared = await AsOneCallerAsync(
            caller => caller.Rules.DefineAsync(Asked(leaving, staying), Cancel));

        await AsOneCallerAsync(caller => caller.Destinations.RemoveAsync(leaving, Cancel));

        NotificationRule left = Assert.Single(await HeldRulesAsync());

        Assert.Equal(shared.Data!.Id, left.Id);
        Assert.Equal([staying], left.Destinations);
    }

    [Fact(DisplayName = "BR-ND-013: removing a destination takes it out of the programmes that wait, withdraws one left with nowhere to send, and adds no notice")]
    public async Task RemovingADestinationTakesItOutOfTheProgrammesThatWait()
    {
        await EmptyAsync();
        NotificationDestinationId leaving = await RegisteredAsync("Leaving");
        NotificationDestinationId staying = await RegisteredAsync("Staying");
        ProgrammeWatch both = await AskedToBeToldAsync(4001, leaving, staying);
        ProgrammeWatch only = await AskedToBeToldAsync(4002, leaving);
        clock.Turn(TimeSpan.FromMinutes(5));

        await AsOneCallerAsync(caller => caller.Destinations.RemoveAsync(leaving, Cancel));

        await using CarinaDbContext context = Context();
        ProgrammeWatchRepository watches = new(context);
        ProgrammeWatch shared = (await watches.FindAsync(both.Id, Cancel))!;
        ProgrammeWatch alone = (await watches.FindAsync(only.Id, Cancel))!;

        Assert.Equal(ProgrammeWatchState.Waiting, shared.State);
        Assert.Equal([staying], shared.Destinations);
        Assert.Equal(Noon.AddMinutes(5), shared.ChangedAt);
        Assert.Equal(ProgrammeWatchState.Cancelled, alone.State);
        Assert.Empty(alone.Destinations);
        Assert.Equal(0, await context.Set<Notice>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "BR-ND-013: a destination whose programmes cannot be written is not removed, and stays in its sets of rules")]
    public async Task ADestinationWhoseProgrammesCannotBeWrittenIsNotRemoved()
    {
        await EmptyAsync();
        NotificationDestinationId leaving = await RegisteredAsync("Leaving");
        await AskedToBeToldAsync(4001, leaving);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsOneCallerAsync(caller => caller.Destinations.RemoveAsync(leaving, Cancel), watchesCanBeSaved: false));

        Assert.Single(await HeldDestinationsAsync());
        Assert.Equal([leaving], Assert.Single(await HeldRulesAsync()).Destinations);
    }

    [Fact(DisplayName = "D-9: a first destination whose set of rules cannot be written is not registered either")]
    public async Task AFirstDestinationWhoseSetOfRulesCannotBeWrittenIsNotRegistered()
    {
        await EmptyAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsOneCallerAsync(caller => caller.Destinations.RegisterAsync("Somewhere", Address, Cancel), rulesCanBeAdded: false));

        Assert.Empty(await HeldDestinationsAsync());
        Assert.Empty(await HeldRulesAsync());
    }

    private static NotificationRuleChange Asked(params NotificationDestinationId[] destinations)
        => new("A made-up set", null, [NoticeKind.RecordingFailed], [.. destinations.Select(destination => destination.Value)]);

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<T> AsOneCallerAsync<T>(Func<Caller, Task<T>> ask, bool rulesCanBeAdded = true, bool watchesCanBeSaved = true)
    {
        await using CarinaDbContext context = Context();
        NotificationDestinationRepository destinations = new(context);
        NotificationRuleRepository held = new(context);
        INotificationRuleRepository rules = rulesCanBeAdded ? held : new RulesNobodyCanAdd(held);
        ProgrammeWatchRepository asked = new(context);
        IProgrammeWatchRepository watches = watchesCanBeSaved ? asked : new WatchesNobodyCanSave(asked);
        DatabaseAtomicWrite writes = new(context);
        NoticeDispatch dispatch = new(
            new NoticeLine(new SystemNoticeNetwork(), "Carina/0.0.0-test", clock, NoticeLine.UsualDeadline),
            Settings,
            clock);

        return await ask(
            new Caller(
                new NotificationDestinationService(
                    destinations,
                    rules,
                    new NotificationDeliveryRepository(context),
                    watches,
                    writes,
                    seal,
                    dispatch,
                    new NoticeTrials(Settings, clock),
                    NoticeLinkBase.None,
                    clock),
                new NotificationRuleService(rules, destinations, writes, clock)));
    }

    private async Task WaitsItsTurnAsync<T>(Func<Task<T>> write)
    {
        await using CarinaDbContext first = Context();
        await using IDbContextTransaction holding = await first.Database.BeginTransactionAsync(Cancel);

        await new NotificationRuleRepository(first).TakeTurnAsync(Cancel);

        Task<T> next = write();

        Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await holding.CommitAsync(Cancel);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
    }

    private async Task<NotificationDestinationId> RegisteredAsync(string name)
    {
        ServiceResult<ListedNotificationDestination, NotificationFailure> answer =
            await AsOneCallerAsync(caller => caller.Destinations.RegisterAsync(name, Address, Cancel));

        return answer.Data!.Destination.Id;
    }

    private async Task RulesAreHeldAsync(NotificationDestinationId destination, int count)
    {
        await using CarinaDbContext context = Context();
        NotificationRuleRepository rules = new(context);

        for (int index = 0; index < count; index++)
        {
            await rules.AddAsync(
                NotificationRule.Define(
                    NotificationRuleId.New(),
                    new NotificationName($"A made-up set {index}"),
                    [NoticeKind.RecordingFailed],
                    [destination],
                    Noon),
                Cancel);
        }
    }

    private async Task<ProgrammeWatch> AskedToBeToldAsync(int carried, params NotificationDestinationId[] destinations)
    {
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32_736), new ServiceId(1024), new EventId(carried)),
            new WatchedBroadcast(Noon.AddHours(2), Noon.AddHours(3), "作り物の番組", "作り物の局"),
            3,
            destinations,
            Noon);

        await using CarinaDbContext context = Context();
        await new ProgrammeWatchRepository(context).AddAsync(watch, Cancel);

        return watch;
    }

    private async Task<Notice> ToldAsync(params NotificationDestinationId[] destinations)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingCompleted,
            Guid.NewGuid(),
            Noon,
            new NoticeWords("録画が終わりました", "作り物の番組\n作り物の局", Link: null),
            Noon);

        await using CarinaDbContext context = Context();
        await new NoticeRepository(context).AddAsync(
            notice,
            [.. destinations.Select(destination => NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, Noon))],
            Cancel);

        return notice;
    }

    private async Task<IReadOnlyList<NotificationRule>> HeldRulesAsync()
    {
        await using CarinaDbContext context = Context();

        return await new NotificationRuleRepository(context).ListAsync(Cancel);
    }

    private async Task<IReadOnlyList<NotificationDestination>> HeldDestinationsAsync()
    {
        await using CarinaDbContext context = Context();

        return await new NotificationDestinationRepository(context).ListAsync(Cancel);
    }

    private async Task EmptyAsync()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlCommand emptying = new(
            "DELETE FROM notification_event; DELETE FROM notification_rule; DELETE FROM notification_destination; "
            + "DELETE FROM notification_watch;",
            connection);
        await emptying.ExecuteNonQueryAsync(Cancel);
    }

    private sealed record Caller(NotificationDestinationService Destinations, NotificationRuleService Rules);

    private sealed class RulesNobodyCanAdd(INotificationRuleRepository held) : INotificationRuleRepository
    {
        public Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken)
            => held.FindAsync(id, cancellationToken);

        public Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
            => held.ListAsync(cancellationToken);

        public Task TakeTurnAsync(CancellationToken cancellationToken) => held.TakeTurnAsync(cancellationToken);

        public Task AddAsync(NotificationRule rule, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This ledger takes no set of rules.");

        public Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken)
            => held.SaveAsync(rule, cancellationToken);

        public Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken)
            => held.RemoveAsync(rule, cancellationToken);
    }

    private sealed class WatchesNobodyCanSave(IProgrammeWatchRepository held) : IProgrammeWatchRepository
    {
        public Task<ProgrammeWatch?> FindAsync(ProgrammeWatchId id, CancellationToken cancellationToken)
            => held.FindAsync(id, cancellationToken);

        public Task<ProgrammeWatch?> FindOfAsync(ProgrammeId programme, CancellationToken cancellationToken)
            => held.FindOfAsync(programme, cancellationToken);

        public Task<IReadOnlyList<ProgrammeWatch>> ListAsync(CancellationToken cancellationToken)
            => held.ListAsync(cancellationToken);

        public Task<IReadOnlyList<ProgrammeWatch>> ListFollowedAsync(DateTime at, CancellationToken cancellationToken)
            => held.ListFollowedAsync(at, cancellationToken);

        public Task AddAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => held.AddAsync(watch, cancellationToken);

        public Task SaveAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This ledger keeps its programmes as they are.");

        public Task RemoveAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => held.RemoveAsync(watch, cancellationToken);

        public Task<int> RemoveSettledBeforeAsync(DateTime before, CancellationToken cancellationToken)
            => held.RemoveSettledBeforeAsync(before, cancellationToken);
    }
}
