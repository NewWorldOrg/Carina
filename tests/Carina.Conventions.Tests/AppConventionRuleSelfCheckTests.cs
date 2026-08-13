using Carina.Conventions.Tests.Fixtures;
using Carina.Conventions.Tests.Fixtures.Services;

namespace Carina.Conventions.Tests;

public sealed class AppConventionRuleSelfCheckTests
{
    [Fact]
    public void DetectsAControllerWithMoreThanOneAction()
    {
        var violations = ConventionRules.ControllersWithoutASingleInvokeAction(
            [typeof(TwoActionController), typeof(SingleActionController)]);

        Assert.Equal([typeof(TwoActionController).FullName!], violations);
    }

    [Fact]
    public void DetectsAControllerWhoseActionIsNotCalledInvoke()
    {
        var violations = ConventionRules.ControllersWithoutASingleInvokeAction(
            [typeof(MisnamedActionController), typeof(SingleActionController)]);

        Assert.Equal([typeof(MisnamedActionController).FullName!], violations);
    }

    [Fact]
    public void DetectsAServiceMethodThatBypassesServiceResult()
    {
        var violations = ConventionRules.ServiceMethodsNotReturningAServiceResult(
            [typeof(RogueService), typeof(CompliantService)]);

        Assert.Equal([$"{typeof(RogueService).FullName}.Describe"], violations);
    }

    [Fact]
    public void DetectsAValueObjectWithASetter()
    {
        var violations = ConventionRules.MutableValueObjects([typeof(MutableTag), typeof(ImmutableTag)]);

        Assert.Equal([typeof(MutableTag).FullName!], violations);
    }

    [Fact]
    public void DetectsARehydratableTypeWithAPublicConstructor()
    {
        var violations = ConventionRules.RehydratableTypesWithAPublicConstructor(
            [typeof(LeakyEntity), typeof(GuardedEntity)]);

        Assert.Equal([typeof(LeakyEntity).FullName!], violations);
    }
}
