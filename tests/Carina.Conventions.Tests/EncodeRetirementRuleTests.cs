using System.Reflection;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Infrastructure.Persistence;

namespace Carina.Conventions.Tests;

public sealed class EncodeRetirementRuleTests
{
    private static readonly IReadOnlyList<Assembly> Production =
    [
        typeof(Program).Assembly,
        typeof(CarinaDbContext).Assembly,
        typeof(CommonValueObject<>).Assembly,
    ];

    [Fact(DisplayName = "BR-ED2-015: one place takes a profile out of the ledger, and it is the one that retires a used profile instead")]
    public void OnlyOnePlaceTakesAProfileOutOfTheLedger()
    {
        Assert.Equal(
            ["Carina.Api.Services.EncodeProfileService.RemoveAsync"],
            CallSiteCensus.CallersOf(Production, typeof(IEncodeProfileRepository), nameof(IEncodeProfileRepository.RemoveAsync)));
    }

    [Fact(DisplayName = "BR-ED2-015: one place takes a destination out of the ledger")]
    public void OnlyOnePlaceTakesADestinationOutOfTheLedger()
    {
        Assert.Equal(
            ["Carina.Api.Services.EncodeDestinationService.RemoveAsync"],
            CallSiteCensus.CallersOf(
                Production,
                typeof(IEncodeDestinationRepository),
                nameof(IEncodeDestinationRepository.RemoveAsync)));
    }

    [Fact(DisplayName = "BR-EA2-003: the job ledger has no way in that takes a row out of it")]
    public void TheJobLedgerHasNoWayInThatTakesARowOutOfIt()
    {
        Assert.DoesNotContain(
            typeof(IEncodeJobRepository).GetMethods(),
            method => method.Name.Contains("Remove", StringComparison.Ordinal)
                || method.Name.Contains("Delete", StringComparison.Ordinal)
                || method.Name.Contains("Discard", StringComparison.Ordinal));
    }
}
