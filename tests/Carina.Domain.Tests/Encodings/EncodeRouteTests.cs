using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeRouteTests
{
    [Fact(DisplayName = "BR-EV-004: a run that ran where it was sent did not swerve, and one that ran elsewhere says why; neither can be written the other way")]
    public void ARouteSwervesExactlyWhenItRanSomewhereElse()
    {
        var asAsked = new EncodeRoute(EncodeEncoder.Software, EncodeEncoder.Software, null);
        var swerved = new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardCannotDoThisCodec);

        Assert.False(asAsked.WasDegraded);
        Assert.True(swerved.WasDegraded);
        Assert.Throws<ArgumentException>(() => new EncodeRoute(EncodeEncoder.Software, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach));
        Assert.Throws<ArgumentException>(() => new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, (EncodeSwerve)9));
    }

    [Fact(DisplayName = "BR-EV-004: a route is read off the plan a run was given, and a plan that runs nowhere is no route")]
    public void ARouteIsReadOffThePlan()
    {
        EncodeRoute swerved = EncodeRoute.Of(EncodeEncoder.Vaapi, EncodePlan.Swerving(EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach, "no node"));
        EncodeRoute straight = EncodeRoute.Of(EncodeEncoder.Software, EncodePlan.AsAsked(EncodeEncoder.Software));

        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, swerved.Swerved);
        Assert.Null(straight.Swerved);
        Assert.Throws<ArgumentException>(() => EncodeRoute.Of(EncodeEncoder.Software, EncodePlan.NothingHereCanDoIt("nowhere")));
    }
}

public sealed class EncodeHeadwayTests
{
    private static readonly DateTime At = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-014: headway is read off a progress report, with the moment it was read")]
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

    [Fact(DisplayName = "BR-ED2-014: a portion is between none and all, nothing left is not less than none, and the time is UTC")]
    public void HeadwayHoldsItsShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(1.5, null, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(-0.1, null, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeHeadway(0.5, TimeSpan.FromSeconds(-1), At));
        Assert.Throws<ArgumentException>(() => new EncodeHeadway(0.5, null, DateTime.SpecifyKind(At, DateTimeKind.Local)));
    }
}
