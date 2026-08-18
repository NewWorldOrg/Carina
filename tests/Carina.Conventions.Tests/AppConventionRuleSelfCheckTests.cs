using Carina.Conventions.Tests.Fixtures;
using Carina.Conventions.Tests.Fixtures.Services;

namespace Carina.Conventions.Tests;

public sealed class AppConventionRuleSelfCheckTests
{
    [Fact]
    public void DetectsAControllerWithMoreThanOneAction()
    {
        IReadOnlyList<string> violations = ConventionRules.ControllersWithoutASingleInvokeAction(
            [typeof(TwoActionController), typeof(SingleActionController)]);

        Assert.Equal([typeof(TwoActionController).FullName!], violations);
    }

    [Fact]
    public void DetectsAControllerWhoseActionIsNotCalledInvoke()
    {
        IReadOnlyList<string> violations = ConventionRules.ControllersWithoutASingleInvokeAction(
            [typeof(MisnamedActionController), typeof(SingleActionController)]);

        Assert.Equal([typeof(MisnamedActionController).FullName!], violations);
    }

    [Fact]
    public void DetectsAServiceMethodThatBypassesServiceResult()
    {
        IReadOnlyList<string> violations = ConventionRules.ServiceMethodsNotReturningAServiceResult(
            [typeof(RogueService), typeof(CompliantService)]);

        Assert.Equal([$"{typeof(RogueService).FullName}.Describe"], violations);
    }

    [Fact]
    public void DetectsAValueObjectWithASetter()
    {
        IReadOnlyList<string> violations = ConventionRules.MutableValueObjects([typeof(MutableTag), typeof(ImmutableTag)]);

        Assert.Equal([typeof(MutableTag).FullName!], violations);
    }

    [Fact]
    public void DetectsAControllerDependencyFromOutsideTheServicesNamespace()
    {
        IReadOnlyList<string> violations = ConventionRules.ControllerDependenciesOutsideTheServicesNamespace(
            [typeof(SmugglingController), typeof(SingleActionController)]);

        Assert.Equal(
            [$"{typeof(SmugglingController).FullName}({typeof(StrayDependency).FullName})"],
            violations);
    }

    [Fact]
    public void DetectsARehydratableTypeWithAPublicConstructor()
    {
        IReadOnlyList<string> violations = ConventionRules.RehydratableTypesWithAPublicConstructor(
            [typeof(LeakyEntity), typeof(GuardedEntity)]);

        Assert.Equal([typeof(LeakyEntity).FullName!], violations);
    }
}
