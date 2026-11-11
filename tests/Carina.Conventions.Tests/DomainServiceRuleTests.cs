using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

namespace Carina.Conventions.Tests;

public sealed class DomainServiceRuleTests
{
    private static readonly IReadOnlyList<Type> ProductionTypes =
    [
        .. typeof(Program).Assembly.GetTypes(),
        .. typeof(CommonValueObject<>).Assembly.GetTypes(),
        .. typeof(CarinaDbContext).Assembly.GetTypes(),
    ];

    [Fact(DisplayName = "a class of the domain that takes a repository or the clock through its constructor is a domain service")]
    public void ADomainClassThatTakesAPortIsADomainService()
    {
        Assert.Empty(DomainServiceRules.DomainClassesTakingAPortUnderAnotherName(ProductionTypes, DomainServiceRules.Domain));
    }

    [Fact(DisplayName = "neither the application services nor the infrastructure declare a domain service")]
    public void NothingOutsideTheDomainIsADomainService()
    {
        Assert.Empty(DomainServiceRules.DomainServicesOutsideTheDomain(ProductionTypes, DomainServiceRules.Domain));
    }

    [Fact(DisplayName = "a domain service is a sealed class that is not static and takes only interfaces of the domain and the clock")]
    public void ADomainServiceIsASealedClassTakingOnlyPorts()
    {
        Assert.Empty(DomainServiceRules.MisshapenDomainServices(ProductionTypes, DomainServiceRules.Domain));
    }

    [Fact(DisplayName = "a domain service is declared in the domain, in a file of its own name")]
    public void ADomainServiceIsDeclaredInAFileOfItsOwnName()
    {
        Assert.Empty(DomainServiceRules.DomainServicesOutOfPlace(RepositoryPaths.SourceDirectory));
    }

    [Fact(DisplayName = "the rules have a domain service of the production code to bite on")]
    public void TheRulesHaveADomainServiceToBiteOn()
    {
        Assert.Contains(
            typeof(ProgrammeEndDomainService).FullName!,
            DomainServiceRules.DomainServices(ProductionTypes, DomainServiceRules.Domain));
    }
}
