using System.Reflection;

using Carina.Domain.Base;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Conventions.Tests;

public sealed class RuleRetirementRuleTests
{
    private const string WhereARuleIsRetired = "Carina.Infrastructure.Rules.RuleApplicationService.RetiredAsync";

    private static readonly IReadOnlyList<Assembly> Production =
    [
        typeof(Program).Assembly,
        typeof(CarinaDbContext).Assembly,
        typeof(CommonValueObject<>).Assembly,
    ];

    [Fact]
    public void OnlyOnePlaceInTheApplicationTakesARuleOutOfTheLedger()
    {
        Assert.Equal(
            [WhereARuleIsRetired],
            CallSiteCensus.CallersOf(Production, typeof(IRuleRepository), nameof(IRuleRepository.RemoveAsync)));
    }

    [Fact]
    public void ThatOnePlaceWithdrawsWhatTheRuleMadeBeforeItTakesTheRuleAway()
    {
        Assert.Contains(
            WhereARuleIsRetired,
            CallSiteCensus.CallersOf(
                Production,
                typeof(RuleApplicationService),
                nameof(RuleApplicationService.DroppedAsync)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void NothingReachesAroundTheRepositoryToTakeARowOutOfTheLedger()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure.Persistence.Repositories.ReservationRepository.WithdrawAsync",
                "Carina.Infrastructure.Persistence.Repositories.RuleRepository.RemoveAsync",
            ],
            Removing());
    }

    [Fact]
    public void TheCensusReadsTheAssembliesTheApplicationIsMadeOf()
    {
        Assert.Equal(
            ["Carina.Api", "Carina.Domain", "Carina.Infrastructure"],
            Production.Select(assembly => assembly.GetName().Name!).Order(StringComparer.Ordinal));

        Assert.True(CallSiteCensus.MethodsRead(Production) > 0);
    }

    private static IReadOnlyList<string> Removing()
        =>
        [
            .. CallSiteCensus.CallersOf(Production, typeof(DbContext), "Remove")
                .Concat(CallSiteCensus.CallersOf(Production, typeof(DbContext), "RemoveRange"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
}
