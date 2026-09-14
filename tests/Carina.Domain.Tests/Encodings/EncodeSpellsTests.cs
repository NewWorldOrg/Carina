using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeSpellsTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a ledger with nothing finished says so rather than answering nought seconds")]
    public void ALedgerWithNothingFinishedSaysSoRatherThanAnsweringNought()
    {
        EncodeSpells spells = EncodeSpells.Of([]);

        Assert.Equal(0, spells.Counted);
        Assert.Null(spells.Average);
        Assert.Null(spells.Oldest);
        Assert.Null(spells.Newest);
        Assert.False(spells.CanBeAveraged);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnAverageIsNotMadeOutOfFewerJobsThanItTakes(int finished)
    {
        EncodeSpells spells = EncodeSpells.Of(
            [.. Enumerable.Range(0, finished).Select(each => new EncodeSpell(Noon.AddHours(each), TimeSpan.FromMinutes(30)))]);

        Assert.Equal(finished, spells.Counted);
        Assert.Null(spells.Average);
        Assert.False(spells.CanBeAveraged);
        Assert.Equal(Noon, spells.Oldest);
    }

    [Fact(DisplayName = "with enough of them the average is the mean of what was counted")]
    public void WithEnoughOfThemTheAverageIsTheMeanOfWhatWasCounted()
    {
        EncodeSpells spells = EncodeSpells.Of(
        [
            new EncodeSpell(Noon, TimeSpan.FromMinutes(10)),
            new EncodeSpell(Noon.AddHours(1), TimeSpan.FromMinutes(20)),
            new EncodeSpell(Noon.AddHours(2), TimeSpan.FromMinutes(30)),
        ]);

        Assert.Equal(3, spells.Counted);
        Assert.Equal(TimeSpan.FromMinutes(20), spells.Average);
        Assert.True(spells.CanBeAveraged);
    }

    [Fact(DisplayName = "the window is the two ends of what was counted, whatever order it arrived in")]
    public void TheWindowIsTheTwoEndsOfWhatWasCounted()
    {
        EncodeSpells spells = EncodeSpells.Of(
        [
            new EncodeSpell(Noon.AddHours(2), TimeSpan.FromMinutes(30)),
            new EncodeSpell(Noon, TimeSpan.FromMinutes(10)),
            new EncodeSpell(Noon.AddHours(1), TimeSpan.FromMinutes(20)),
        ]);

        Assert.Equal(Noon, spells.Oldest);
        Assert.Equal(Noon.AddHours(2), spells.Newest);
    }

    [Fact]
    public void AJobCannotHaveTakenLessThanNoTimeAtAll()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeSpell(Noon, TimeSpan.FromSeconds(-1)));

    [Fact]
    public void ASpellIsTimedInUtc()
        => Assert.Throws<ArgumentException>(
            () => new EncodeSpell(new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local), TimeSpan.FromMinutes(1)));
}
