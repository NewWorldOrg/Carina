using System.Reflection;

using Carina.Domain.Base;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Rules;

namespace Carina.Conventions.Tests;

public sealed class RulePrecedenceRuleTests
{
    private static readonly IReadOnlyList<Assembly> Production =
    [
        typeof(Program).Assembly,
        typeof(CarinaDbContext).Assembly,
        typeof(CommonValueObject<>).Assembly,
    ];

    [Fact]
    public void OnlyOnePlaceInTheApplicationSaysWhichRuleComesFirst()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure.Persistence.Repositories.RuleRepository.ListAsync",
                "Carina.Infrastructure.Persistence.Repositories.RuleRepository.ListEnabledByPrecedenceAsync",
                "Carina.Infrastructure.Rules.RuleMatcher.AgainstAsync",
            ],
            CallSiteCensus.CallersOf(Production, typeof(RuleMatcher), nameof(RuleMatcher.InPrecedence)));
    }

    [Fact]
    public void EveryWayIntoTheRulesGoesThroughTheOrderingRatherThanAroundIt()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure.Rules.RuleApplicationService.ApplyAsync",
                "Carina.Infrastructure.Rules.RuleApplicationService.RehearsedAsync",
            ],
            CallSiteCensus.CallersOf(
                Production,
                typeof(Carina.Domain.Rules.IRuleRepository),
                nameof(Carina.Domain.Rules.IRuleRepository.ListEnabledByPrecedenceAsync)));
    }

    [Fact]
    public void TheCensusReadsTheAssembliesTheApplicationIsMadeOf()
    {
        Assert.Equal(
            ["Carina.Api", "Carina.Domain", "Carina.Infrastructure"],
            Production.Select(assembly => assembly.GetName().Name!).Order(StringComparer.Ordinal));

        Assert.True(CallSiteCensus.MethodsRead(Production) > 0);
    }
}
