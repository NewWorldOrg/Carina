using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Notifications;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.DbIntegration.App.Notifications;

[Collection(AppDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationRuleServiceTests(AppDatabase database) : IAsyncLifetime
{
    private const int AtOnce = NotificationWrites.AtOnce;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly NotificationWrites writes = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await writes.DisposeAsync();

    [Fact(DisplayName = "BR-NT-006: of the sets of rules defined at the same moment beside nineteen, one is kept and the rest are refused")]
    public async Task OfTheSetsOfRulesDefinedAtTheSameMomentBesideNineteenOneIsKept()
    {
        await writes.EmptyAsync();
        NotificationDestinationId destination = await writes.RegisteredAsync("Somewhere");
        await writes.RulesAreHeldAsync(destination, NotificationRule.MostRules - 2);

        ServiceResult<NotificationRule, NotificationFailure>[] answers = await Task.WhenAll(
            Enumerable.Range(0, AtOnce).Select(_ => AsOneCallerAsync(rules => rules.DefineAsync(NotificationWrites.Asked(destination), Cancel))));

        Assert.Single(answers, answer => answer.IsSuccess);
        Assert.All(
            answers.Where(answer => !answer.IsSuccess),
            answer => Assert.Equal(NotificationFailure.TooManyRules, answer.ErrorType));
        Assert.Equal(NotificationRule.MostRules, (await writes.HeldRulesAsync()).Count);
    }

    [Fact(DisplayName = "BR-NT-006: a set of rules is not defined while another writer of the notifications holds its turn")]
    public async Task ASetOfRulesIsNotDefinedWhileAnotherWriterHoldsItsTurn()
    {
        await writes.EmptyAsync();
        NotificationDestinationId destination = await writes.RegisteredAsync("Somewhere");

        await writes.WaitsItsTurnAsync(() => AsOneCallerAsync(rules => rules.DefineAsync(NotificationWrites.Asked(destination), Cancel)));

        Assert.Equal(2, (await writes.HeldRulesAsync()).Count);
    }

    private async Task<T> AsOneCallerAsync<T>(Func<NotificationRuleService, Task<T>> ask, bool rulesCanBeAdded = true, bool watchesCanBeSaved = true)
    {
        await using AsyncServiceScope scope = writes.Scope(rulesCanBeAdded, watchesCanBeSaved);

        return await ask(scope.ServiceProvider.GetRequiredService<NotificationRuleService>());
    }
}
