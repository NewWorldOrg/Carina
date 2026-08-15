using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Infrastructure.Persistence;

namespace Carina.Conventions.Tests;

public sealed class AppEventRuleTests
{
    private static readonly IReadOnlyList<Type> ProductionTypes =
    [
        .. typeof(ServiceResult).Assembly.GetTypes(),
        .. typeof(CommonValueObject<>).Assembly.GetTypes(),
        .. typeof(CarinaDbContext).Assembly.GetTypes(),
        .. typeof(AppEventName).Assembly.GetTypes(),
    ];

    [Fact]
    public void EverySignalNamesItsEventFromTheDeclaredSet()
    {
        Assert.Empty(AppEventRules.SignalsThatAcceptANameOutsideTheSet(ProductionTypes));
    }

    [Fact]
    public void NoSignalCarriesAPayload()
    {
        Assert.Empty(AppEventRules.SignalsThatCarryAPayload(ProductionTypes));
    }

    [Fact]
    public void TheRulesHaveASignallingSeamToBiteOn()
    {
        Assert.Contains(
            $"{typeof(IAppEventPublisher).FullName}.Signal(AppEventName)",
            AppEventRules.AppEventSignals(ProductionTypes));
    }
}
