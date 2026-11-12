using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.DbIntegration.App.Notifications;

[Collection(AppDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationPushServiceTests(AppDatabase database) : IAsyncLifetime
{
    private const string Endpoint = NotificationWrites.Endpoint;

    private const int AtOnce = NotificationWrites.AtOnce;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly NotificationWrites writes = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await writes.DisposeAsync();

    [Fact(DisplayName = "BR-ND-015: one endpoint registered at the same moment by several callers is held in one row, and makes one set of rules")]
    public async Task OneEndpointRegisteredAtTheSameMomentIsHeldInOneRow()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();

        ServiceResult<PushEnrolled, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(_ => AsOneCallerAsync(push => push.SubscribeAsync(
                "A made-up browser",
                Endpoint,
                browser.KeyAsWritten,
                browser.SecretAsWritten,
                Cancel))));

        NotificationDestination held = Assert.Single(await writes.HeldDestinationsAsync());

        Assert.All(answers, answer => Assert.True(answer.IsSuccess));
        Assert.Single(answers, answer => answer.Data!.Added);
        Assert.All(answers, answer => Assert.Equal(held.Id, answer.Data!.Destination.Destination.Id));
        Assert.Equal(WebhookTarget.Push, held.Target);
        Assert.Equal([held.Id], Assert.Single(await writes.HeldRulesAsync()).Destinations);
    }

    [Fact(DisplayName = "D-9: browsers registered at the same moment into an empty ledger make one set of rules between them")]
    public async Task BrowsersRegisteredAtTheSameMomentMakeOneSetOfRulesBetweenThem()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();

        ServiceResult<PushEnrolled, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(index => AsOneCallerAsync(push => push.SubscribeAsync(
                "A made-up browser",
                $"{Endpoint}-{index}",
                browser.KeyAsWritten,
                browser.SecretAsWritten,
                Cancel))));

        NotificationRule made = Assert.Single(await writes.HeldRulesAsync());

        Assert.All(answers, answer => Assert.True(answer.IsSuccess && answer.Data!.Added));
        Assert.Equal(AtOnce, (await writes.HeldDestinationsAsync()).Count);
        Assert.Contains(Assert.Single(made.Destinations), answers.Select(answer => answer.Data!.Destination.Destination.Id));
    }

    [Fact(DisplayName = "BR-NT-006: a browser is not registered while another writer of the notifications holds its turn")]
    public async Task ABrowserIsNotRegisteredWhileAnotherWriterHoldsItsTurn()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();

        await writes.WaitsItsTurnAsync(() => AsOneCallerAsync(push => push.SubscribeAsync(
            "A made-up browser",
            Endpoint,
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel)));

        Assert.Single(await writes.HeldDestinationsAsync());
    }

    [Fact(DisplayName = "BR-ND-015: a row given the endpoint another row holds is refused, and both rows keep what they held")]
    public async Task ARowGivenTheEndpointAnotherRowHoldsIsRefused()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();
        ServiceResult<PushEnrolled, NotificationFailure> first = await AsOneCallerAsync(push => push.SubscribeAsync(
            "A made-up browser",
            Endpoint,
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));
        ServiceResult<PushEnrolled, NotificationFailure> second = await AsOneCallerAsync(push => push.SubscribeAsync(
            "Another made-up browser",
            $"{Endpoint}-other",
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));
        IReadOnlyList<NotificationDestination> before = await writes.HeldDestinationsAsync();

        ServiceResult<ListedNotificationDestination, NotificationFailure> answer = await AsOneCallerAsync(push => push.ResubscribeAsync(
            second.Data!.Destination.Destination.Id,
            Endpoint,
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));

        IReadOnlyList<NotificationDestination> after = await writes.HeldDestinationsAsync();

        Assert.True(first.IsSuccess);
        Assert.False(answer.IsSuccess);
        Assert.Equal(NotificationFailure.HeldByAnother, answer.ErrorType);
        Assert.Equal(
            before.Select(held => (held.Id, held.EndpointHash, Convert.ToHexString(held.SealedUrl))),
            after.Select(held => (held.Id, held.EndpointHash, Convert.ToHexString(held.SealedUrl))));
    }

    [Fact(DisplayName = "BR-ND-015: a row given a new endpoint holds it from then on, and the endpoint it gave up can be registered again")]
    public async Task ARowGivenANewEndpointHoldsItFromThenOn()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();
        ServiceResult<PushEnrolled, NotificationFailure> first = await AsOneCallerAsync(push => push.SubscribeAsync(
            "A made-up browser",
            Endpoint,
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));

        ServiceResult<ListedNotificationDestination, NotificationFailure> moved = await AsOneCallerAsync(push => push.ResubscribeAsync(
            first.Data!.Destination.Destination.Id,
            $"{Endpoint}-renewed",
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));
        ServiceResult<PushEnrolled, NotificationFailure> again = await AsOneCallerAsync(push => push.SubscribeAsync(
            "Another made-up browser",
            Endpoint,
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            Cancel));

        Assert.True(moved.IsSuccess);
        Assert.True(again.IsSuccess && again.Data!.Added);
        Assert.Equal(2, (await writes.HeldDestinationsAsync()).Count);
        Assert.Single(await writes.HeldRulesAsync());
    }

    [Fact(DisplayName = "D-9: a first browser whose set of rules cannot be written is not registered either")]
    public async Task AFirstBrowserWhoseSetOfRulesCannotBeWrittenIsNotRegistered()
    {
        await writes.EmptyAsync();
        using PushReceiver browser = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsOneCallerAsync(
                push => push.SubscribeAsync("A made-up browser", Endpoint, browser.KeyAsWritten, browser.SecretAsWritten, Cancel),
                rulesCanBeAdded: false));

        Assert.Empty(await writes.HeldDestinationsAsync());
        Assert.Empty(await writes.HeldRulesAsync());
    }

    private async Task<T> AsOneCallerAsync<T>(Func<NotificationPushService, Task<T>> ask, bool rulesCanBeAdded = true, bool watchesCanBeSaved = true)
    {
        await using AsyncServiceScope scope = writes.Scope(rulesCanBeAdded, watchesCanBeSaved);

        return await ask(scope.ServiceProvider.GetRequiredService<NotificationPushService>());
    }
}
