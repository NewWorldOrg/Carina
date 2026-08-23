using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Rules;

public sealed class RuleTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARuleCanBeDraftedDisabledAndTurnedOnLater()
    {
        Rule rule = Draft(enabled: false);

        Assert.False(rule.Enabled);

        rule.Enable();
        Assert.True(rule.Enabled);

        rule.Disable();
        Assert.False(rule.Enabled);
    }

    [Fact]
    public void ARuleKeepsTheQueryVerbatim()
    {
        const string Asked = "keyword=%E3%83%89%E3%83%A9%E3%83%9E&genre=7&genre=9&type=terrestrial";

        Rule rule = Rule.Draft(
            RuleId.New(),
            "Drama",
            new RuleQuery(Asked),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now);

        Assert.Equal(Asked, rule.Query.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("?keyword=a")]
    [InlineData("keyword=a#top")]
    [InlineData("keyword=a&&genre=7")]
    [InlineData("=7")]
    public void AQueryThatIsNotTheOneAProgrammeSearchCarriesIsRefused(string asked)
        => Assert.Throws<ArgumentException>(() => new RuleQuery(asked));

    [Fact]
    public void ARuleIsNamed()
    {
        Assert.Throws<ArgumentException>(() => Draft(name: " "));
        Assert.Throws<ArgumentException>(() => Draft(name: new string('x', Rule.NameMaxLength + 1)));
    }

    [Fact]
    public void RewritingARuleKeepsItsIdentityAndItsEnabledState()
    {
        Rule rule = Draft(enabled: false);
        RuleId id = rule.Id;

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), new Priority(30), Margin.OfSeconds(10), Margin.OfSeconds(20));

        Assert.Equal(id, rule.Id);
        Assert.False(rule.Enabled);
        Assert.Equal("Renamed", rule.Name);
        Assert.Equal(30, rule.Priority.Value);
        Assert.Equal(10, rule.MarginBefore.Seconds);
    }

    private static Rule Draft(string name = "Drama", bool enabled = true)
        => Rule.Draft(
            RuleId.New(),
            name,
            new RuleQuery("keyword=drama"),
            Priority.Default,
            enabled,
            Margin.None,
            Margin.None,
            Now);
}
