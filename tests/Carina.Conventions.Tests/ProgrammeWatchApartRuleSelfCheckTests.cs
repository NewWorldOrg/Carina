using Carina.Conventions.Tests.Fixtures;
using Carina.Domain.Notifications;

namespace Carina.Conventions.Tests;

public sealed class ProgrammeWatchApartRuleSelfCheckTests
{
    private const string Fixture = "Carina.Conventions.Tests.Fixtures.ProgrammeWatchFixtures";

    private static readonly HashSet<Type> Named = [typeof(ProgrammeWatch), typeof(IProgrammeWatchRepository)];

    private static readonly IReadOnlyList<string> Seen = TypeMentionCensus.MentionsOf(
        TypeMentionCensus.TypesUnder(
            [typeof(ProgrammeWatchFixtures).Assembly],
            type => type == typeof(ProgrammeWatchFixtures)),
        Named);

    [Theory]
    [InlineData(nameof(ProgrammeWatchFixtures.TakesOneAsAnArgument))]
    [InlineData(nameof(ProgrammeWatchFixtures.TakesSeveralInAList))]
    [InlineData(nameof(ProgrammeWatchFixtures.AsksOneWhetherItStands))]
    [InlineData(nameof(ProgrammeWatchFixtures.ReadsTheLedger))]
    public void DetectsAMentionInWhatIsTakenAndInWhatIsCalled(string method)
    {
        Assert.Contains($"{Fixture}.{method}", Seen, StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsAMentionInsideAMethodThatWaits()
    {
        Assert.Contains(
            Seen,
            where => where.StartsWith(
                $"{Fixture}+<{nameof(ProgrammeWatchFixtures.ReadsTheLedgerAndWaitsAsync)}>",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SaysNothingAboutAMethodThatMentionsNone()
    {
        Assert.DoesNotContain($"{Fixture}.{nameof(ProgrammeWatchFixtures.MentionsNone)}", Seen, StringComparer.Ordinal);
    }

    [Fact]
    public void SaysNothingWhenItIsPointedAtNothing()
    {
        Assert.Empty(TypeMentionCensus.MentionsOf([], Named));
        Assert.Empty(TypeMentionCensus.MentionsOf([typeof(ProgrammeWatchFixtures)], new HashSet<Type>()));
    }
}
