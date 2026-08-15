using Carina.Api.Controllers.DriverStatus;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Conventions.Tests;

public sealed class AppConventionRuleTests
{
    private static readonly IReadOnlyList<Type> ProductionTypes =
    [
        .. typeof(Program).Assembly.GetTypes(),
        .. typeof(CommonValueObject<>).Assembly.GetTypes(),
        .. typeof(CarinaDbContext).Assembly.GetTypes(),
    ];

    [Fact]
    public void ControllersDeclareExactlyOneInvokeAction()
    {
        Assert.Empty(ConventionRules.ControllersWithoutASingleInvokeAction(ProductionTypes));
    }

    [Fact]
    public void ServicesReturnServiceResults()
    {
        Assert.Empty(ConventionRules.ServiceMethodsNotReturningAServiceResult(ProductionTypes));
    }

    [Fact]
    public void ValueObjectsAreImmutable()
    {
        Assert.Empty(ConventionRules.MutableValueObjects(ProductionTypes));
    }

    [Fact]
    public void RehydratableTypesHideTheirConstructors()
    {
        Assert.Empty(ConventionRules.RehydratableTypesWithAPublicConstructor(ProductionTypes));
    }

    [Fact]
    public void ControllersTakeTheirDependenciesFromTheServicesNamespace()
    {
        Assert.Empty(
            ConventionRules.ControllerDependenciesOutsideTheServicesNamespace(ProductionTypes)
        );
    }

    [Fact]
    public void TheRulesHaveProductionInstancesToBiteOn()
    {
        Assert.Contains(typeof(GetDriverStatusAction), ProductionTypes);
        Assert.Contains(typeof(DriverStatusService), ProductionTypes);
        Assert.Contains(typeof(DriverSocketPath), ProductionTypes);
        Assert.Contains(typeof(DriverStatusSnapshot), ProductionTypes);
        Assert.Contains(ProductionTypes, type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);
    }
}
