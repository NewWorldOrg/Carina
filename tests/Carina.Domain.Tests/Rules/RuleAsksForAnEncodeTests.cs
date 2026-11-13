using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Rules;

public sealed class RuleAsksForAnEncodeTests
{
    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a rule that says nothing about encoding asks for one, which is what every rule did before one could say otherwise")]
    public void ARuleThatSaysNothingAsksForAnEncode() => Assert.True(Drafted().EncodeWhenRecorded);

    [Fact(DisplayName = "a rule can be drafted asking for no encode")]
    public void ARuleCanBeDraftedAskingForNoEncode()
        => Assert.False(Drafted(encodeWhenRecorded: false).EncodeWhenRecorded);

    [Fact(DisplayName = "rewriting a rule keeps whatever the rewrite asked for rather than the priority or the margins deciding it")]
    public void RewritingARuleKeepsWhatTheRewriteAskedFor()
    {
        Rule rule = Drafted();

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, false);

        Assert.False(rule.EncodeWhenRecorded);

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, true);

        Assert.True(rule.EncodeWhenRecorded);
    }

    private static Rule Drafted(bool encodeWhenRecorded = true)
        => Rule.Draft(
            RuleId.New(),
            "a rule",
            new RuleQuery("keyword=hill"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Noon,
            encodeWhenRecorded);
}
