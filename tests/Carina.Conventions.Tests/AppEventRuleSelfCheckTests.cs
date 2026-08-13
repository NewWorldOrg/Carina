using Carina.Conventions.Tests.Fixtures.Events;

namespace Carina.Conventions.Tests;

public sealed class AppEventRuleSelfCheckTests
{
    [Fact]
    public void DetectsASignalThatTakesARawNameInsteadOfOneFromTheSet()
    {
        var violations = AppEventRules.SignalsThatAcceptANameOutsideTheSet(
            [typeof(LoosePublisher), typeof(CompliantPublisher)]);

        Assert.Equal([$"{typeof(LoosePublisher).FullName}.Signal(String)"], violations);
    }

    [Fact]
    public void DetectsASignalThatCarriesAPayload()
    {
        var violations = AppEventRules.SignalsThatCarryAPayload(
            [typeof(PayloadPublisher), typeof(CompliantPublisher)]);

        Assert.Equal([$"{typeof(PayloadPublisher).FullName}.Signal(AppEventName, String)"], violations);
    }

    [Fact]
    public void LeavesACompliantPublisherAlone()
    {
        Assert.Empty(AppEventRules.SignalsThatAcceptANameOutsideTheSet([typeof(CompliantPublisher)]));
        Assert.Empty(AppEventRules.SignalsThatCarryAPayload([typeof(CompliantPublisher)]));
    }

    [Fact]
    public void FindsEverySignalAPublisherDeclares()
    {
        Assert.Equal(
            [
                $"{typeof(CompliantPublisher).FullName}.Signal(AppEventName)",
                $"{typeof(CompliantPublisher).FullName}.SignalLater(AppEventName, CancellationToken)",
            ],
            AppEventRules.AppEventSignals([typeof(CompliantPublisher)]));
    }
}
