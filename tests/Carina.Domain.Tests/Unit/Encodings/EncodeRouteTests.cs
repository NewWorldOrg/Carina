using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeRouteTests
{
    [Fact(DisplayName = "a run that ran where it was sent did not swerve, and one that ran elsewhere says why; neither can be written the other way")]
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

    [Fact(DisplayName = "a route to or from an NVIDIA card is kept like any other")]
    public void ARouteToOrFromAnNvidiaCardIsKeptLikeAnyOther()
    {
        var asAsked = new EncodeRoute(EncodeEncoder.Nvenc, EncodeEncoder.Nvenc, null);
        var swerved = new EncodeRoute(EncodeEncoder.Software, EncodeEncoder.Nvenc, EncodeSwerve.TheProcessorCannotDoThisCodec);

        Assert.False(asAsked.WasDegraded);
        Assert.True(swerved.WasDegraded);
        Assert.Equal(EncodeEncoder.Nvenc, swerved.Ran);
    }

    [Fact(DisplayName = "a route is read off the plan a run was given, and a plan that runs nowhere is no route")]
    public void ARouteIsReadOffThePlan()
    {
        EncodeRoute swerved = EncodeRoute.Of(EncodeEncoder.Vaapi, EncodePlan.Swerving(EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach, "no node"));
        EncodeRoute straight = EncodeRoute.Of(EncodeEncoder.Software, EncodePlan.AsAsked(EncodeEncoder.Software));

        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, swerved.Swerved);
        Assert.Null(straight.Swerved);
        Assert.Throws<ArgumentException>(() => EncodeRoute.Of(EncodeEncoder.Software, EncodePlan.NothingHereCanDoIt("nowhere")));
    }
}
