using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class OnTheFlyStartTests
{
    [Fact]
    public void ARefusalNamesNoPathOnThisMachine()
    {
        OnTheFlyStart refused = OnTheFlyStart.Refused(
            OnTheFlyRefusal.NothingCameOut,
            "Error opening input /srv/recordings/a1b2c3.ts: No such file");

        Assert.False(refused.Running);
        Assert.Null(refused.Viewing);
        Assert.Equal(OnTheFlyRefusal.NothingCameOut, refused.Refusal);
        Assert.DoesNotContain('/', refused.Note);
        Assert.Contains("Error opening input", refused.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingIsRefusedForOneOfTheReasonsNamedRatherThanAnyNumberAtAll()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OnTheFlyStart.Refused((OnTheFlyRefusal)99, "no"));
        Assert.Throws<ArgumentNullException>(() => OnTheFlyStart.Started(null!));
    }
}
