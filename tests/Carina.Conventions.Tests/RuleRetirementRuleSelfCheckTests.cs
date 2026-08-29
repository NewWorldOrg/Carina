using System.Reflection;

using Carina.Conventions.Tests.Fixtures;
using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Conventions.Tests;

public sealed class RuleRetirementRuleSelfCheckTests
{
    private const string Fixture = "Carina.Conventions.Tests.Fixtures.RuleRetirementFixtures";

    private static readonly IReadOnlyList<Assembly> Fixtures = [typeof(RuleRetirementFixtures).Assembly];

    [Fact]
    public void DetectsASecondPlaceTakingARuleOutOfTheLedger()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(RuleRetirementFixtures.RemovesARuleWithoutWithdrawingWhatItMade)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(IRuleRepository), nameof(IRuleRepository.RemoveAsync)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsSomethingReachingAroundTheRepositoryOneRowAtATime()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(RuleRetirementFixtures.TakesARuleOutOfTheLedgerBehindTheRepository)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(DbContext), "Remove"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsSomethingReachingAroundTheRepositoryInABatch()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(RuleRetirementFixtures.TakesRulesOutOfTheLedgerInABatchBehindTheRepository)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(DbContext), "RemoveRange"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void SaysNothingAboutAPlaceThatOnlyReadsARule()
    {
        Assert.DoesNotContain(
            $"{Fixture}.{nameof(RuleRetirementFixtures.ReadsARuleWithoutRemovingIt)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(IRuleRepository), nameof(IRuleRepository.RemoveAsync)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void SaysWhatItDoesNotCatchRatherThanClaimingToCatchIt()
    {
        Assert.DoesNotContain(
            $"{Fixture}.{nameof(RuleRetirementFixtures.TakesARuleOutOfTheLedgerWithoutTrackingIt)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(DbContext), "Remove"),
            StringComparer.Ordinal);

        Assert.Contains(
            $"{Fixture}.{nameof(RuleRetirementFixtures.TakesARuleOutOfTheLedgerWithoutTrackingIt)}",
            CallSiteCensus.CallersOf(
                Fixtures,
                typeof(EntityFrameworkQueryableExtensions),
                "ExecuteDeleteAsync"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void SaysNothingAboutTheWithdrawalWhenNobodyCallsIt()
    {
        Assert.Empty(CallSiteCensus.CallersOf(
            Fixtures,
            typeof(RuleApplicationService),
            nameof(RuleApplicationService.DroppedAsync)));
    }
}
