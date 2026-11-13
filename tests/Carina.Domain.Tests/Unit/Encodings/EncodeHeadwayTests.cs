using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeHeadwayTests
{
    private static readonly DateTime At = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "headway is read off a progress report, with the moment it was read")]
    public void HeadwayIsReadOffAProgressReport()
    {
        EncodeHeadway headway = EncodeHeadway.Of(EncodeProgress.Of(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), 2.5, false), At);
        EncodeHeadway unmeasured = EncodeHeadway.Of(EncodeProgress.Of(TimeSpan.FromSeconds(25), null, 0, false), At);

        Assert.Equal(0.25, headway.Portion);
        Assert.Equal(TimeSpan.FromSeconds(30), headway.Left);
        Assert.Equal(At, headway.At);
        Assert.Null(unmeasured.Portion);
        Assert.Null(unmeasured.Left);
    }

    [Fact(DisplayName = "a portion is between none and all, nothing left is not less than none, and the time is UTC")]
    public void HeadwayHoldsItsShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(1.5, null, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(-0.1, null, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(0.5, TimeSpan.FromSeconds(-1), At));
        Assert.Throws<ArgumentException>(() => new EncodeHeadway(0.5, null, DateTime.SpecifyKind(At, DateTimeKind.Local)));
    }
}
