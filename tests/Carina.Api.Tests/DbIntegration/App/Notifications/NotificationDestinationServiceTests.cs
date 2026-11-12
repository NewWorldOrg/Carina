using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.DbIntegration.App.Notifications;

[Collection(AppDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationDestinationServiceTests(AppDatabase database) : IAsyncLifetime
{
    private const string Address = NotificationWrites.Address;

    private const int AtOnce = NotificationWrites.AtOnce;

    private static readonly DateTime Noon = NotificationWrites.Noon;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly NotificationWrites writes = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await writes.DisposeAsync();

    [Fact(DisplayName = "D-9: destinations registered at the same moment into an empty ledger make one set of rules between them")]
    public async Task DestinationsRegisteredAtTheSameMomentMakeOneSetOfRulesBetweenThem()
    {
        await writes.EmptyAsync();

        ServiceResult<ListedNotificationDestination, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(_ => AsOneCallerAsync(destinations => destinations.RegisterAsync("Somewhere", Address, Cancel))));

        NotificationRule made = Assert.Single(await writes.HeldRulesAsync());

        Assert.All(answers, answer => Assert.True(answer.IsSuccess));
        Assert.Equal(AtOnce, (await writes.HeldDestinationsAsync()).Count);
        Assert.Contains(Assert.Single(made.Destinations).Value, answers.Select(answer => answer.Data!.Destination.Id.Value));
    }

    [Fact(DisplayName = "D-9: a destination is not registered while another writer of the notifications holds its turn")]
    public async Task ADestinationIsNotRegisteredWhileAnotherWriterHoldsItsTurn()
    {
        await writes.EmptyAsync();

        await writes.WaitsItsTurnAsync(() => AsOneCallerAsync(destinations => destinations.RegisterAsync("Somewhere", Address, Cancel)));

        Assert.Single(await writes.HeldDestinationsAsync());
    }

    [Fact(DisplayName = "BR-NT-004: a destination is not removed while another writer of the notifications holds its turn")]
    public async Task ADestinationIsNotRemovedWhileAnotherWriterHoldsItsTurn()
    {
        await writes.EmptyAsync();
        NotificationDestinationId destination = await writes.RegisteredAsync("Somewhere");

        await writes.WaitsItsTurnAsync(() => AsOneCallerAsync(destinations => destinations.RemoveAsync(destination, Cancel)));

        Assert.Empty(await writes.HeldDestinationsAsync());
    }

    [Fact(DisplayName = "BR-NS-001: removing a destination takes the deliveries to it with it, and leaves the notices they carried")]
    public async Task RemovingADestinationTakesTheDeliveriesToItWithIt()
    {
        await writes.EmptyAsync();
        NotificationDestinationId leaving = await writes.RegisteredAsync("Leaving");
        NotificationDestinationId staying = await writes.RegisteredAsync("Staying");
        Notice notice = await writes.ToldAsync(leaving, staying);

        ServiceResult<NotificationRemoved, NotificationFailure> answer =
            await AsOneCallerAsync(destinations => destinations.RemoveAsync(leaving, Cancel));

        await using AsyncServiceScope scope = writes.Scope();
        IReadOnlyList<NotificationDelivery> left =
            await scope.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>().ListOfAsync(notice.Id, Cancel);

        Assert.True(answer.IsSuccess);
        Assert.Equal(staying, Assert.Single(left).DestinationId);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<INoticeRepository>().FindAsync(notice.Id, Cancel));
    }

    [Fact(DisplayName = "BR-NT-004: removing a destination takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingADestinationTakesItOutOfTheSetsOfRules()
    {
        await writes.EmptyAsync();
        NotificationDestinationId leaving = await writes.RegisteredAsync("Leaving");
        NotificationDestinationId staying = await writes.RegisteredAsync("Staying");
        ServiceResult<NotificationRule, NotificationFailure> shared = await writes.DefinedAsync(NotificationWrites.Asked(leaving, staying));

        await AsOneCallerAsync(destinations => destinations.RemoveAsync(leaving, Cancel));

        NotificationRule left = Assert.Single(await writes.HeldRulesAsync());

        Assert.Equal(shared.Data!.Id, left.Id);
        Assert.Equal([staying], left.Destinations);
    }

    [Fact(DisplayName = "BR-ND-013: removing a destination takes it out of the programmes that wait, withdraws one left with nowhere to send, and adds no notice")]
    public async Task RemovingADestinationTakesItOutOfTheProgrammesThatWait()
    {
        await writes.EmptyAsync();
        NotificationDestinationId leaving = await writes.RegisteredAsync("Leaving");
        NotificationDestinationId staying = await writes.RegisteredAsync("Staying");
        ProgrammeWatch both = await writes.AskedToBeToldAsync(4001, leaving, staying);
        ProgrammeWatch only = await writes.AskedToBeToldAsync(4002, leaving);
        writes.Clock.Turn(TimeSpan.FromMinutes(5));

        await AsOneCallerAsync(destinations => destinations.RemoveAsync(leaving, Cancel));

        await using AsyncServiceScope scope = writes.Scope();
        IProgrammeWatchRepository watches = scope.ServiceProvider.GetRequiredService<IProgrammeWatchRepository>();
        ProgrammeWatch shared = (await watches.FindAsync(both.Id, Cancel))!;
        ProgrammeWatch alone = (await watches.FindAsync(only.Id, Cancel))!;

        Assert.Equal(ProgrammeWatchState.Waiting, shared.State);
        Assert.Equal([staying], shared.Destinations);
        Assert.Equal(Noon.AddMinutes(5), shared.ChangedAt);
        Assert.Equal(ProgrammeWatchState.Cancelled, alone.State);
        Assert.Empty(alone.Destinations);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CarinaDbContext>().Set<Notice>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "BR-ND-013: a destination whose programmes cannot be written is not removed, and stays in its sets of rules")]
    public async Task ADestinationWhoseProgrammesCannotBeWrittenIsNotRemoved()
    {
        await writes.EmptyAsync();
        NotificationDestinationId leaving = await writes.RegisteredAsync("Leaving");
        await writes.AskedToBeToldAsync(4001, leaving);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsOneCallerAsync(destinations => destinations.RemoveAsync(leaving, Cancel), watchesCanBeSaved: false));

        Assert.Single(await writes.HeldDestinationsAsync());
        Assert.Equal([leaving], Assert.Single(await writes.HeldRulesAsync()).Destinations);
    }

    [Fact(DisplayName = "D-9: a first destination whose set of rules cannot be written is not registered either")]
    public async Task AFirstDestinationWhoseSetOfRulesCannotBeWrittenIsNotRegistered()
    {
        await writes.EmptyAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsOneCallerAsync(destinations => destinations.RegisterAsync("Somewhere", Address, Cancel), rulesCanBeAdded: false));

        Assert.Empty(await writes.HeldDestinationsAsync());
        Assert.Empty(await writes.HeldRulesAsync());
    }

    private async Task<T> AsOneCallerAsync<T>(Func<NotificationDestinationService, Task<T>> ask, bool rulesCanBeAdded = true, bool watchesCanBeSaved = true)
    {
        await using AsyncServiceScope scope = writes.Scope(rulesCanBeAdded, watchesCanBeSaved);

        return await ask(scope.ServiceProvider.GetRequiredService<NotificationDestinationService>());
    }
}
