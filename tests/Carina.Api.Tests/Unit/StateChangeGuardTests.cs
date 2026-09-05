namespace Carina.Api.Tests.Unit;

public sealed class StateChangeGuardTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void BrLa002AMethodThatOnlyReadsIsAskedForNoGuard(string method)
    {
        Assert.Empty(EndpointRules.GuardsRequiredBy(method, carriesABody: false));
        Assert.Empty(EndpointRules.GuardsRequiredBy(method, carriesABody: true));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void BrLa002AMethodAFormCanSendIsAskedForTheOriginAndAJsonBodyWhetherOrNotABodyRides(string method)
    {
        Assert.Equal(
            [StateChangeGuard.Origin, StateChangeGuard.JsonBody],
            EndpointRules.GuardsRequiredBy(method, carriesABody: true));
        Assert.Equal(
            [StateChangeGuard.Origin, StateChangeGuard.JsonBody],
            EndpointRules.GuardsRequiredBy(method, carriesABody: false));
    }

    [Fact]
    public void BrLa002ADeleteWithoutABodyIsAskedForTheOriginInPlaceOfAContentType()
    {
        Assert.Equal([StateChangeGuard.Origin], EndpointRules.GuardsRequiredBy("DELETE", carriesABody: false));
    }

    [Fact]
    public void BrLa002ADeleteThatCarriesABodyIsAskedForBothLikeAnyOtherChange()
    {
        Assert.Equal(
            [StateChangeGuard.Origin, StateChangeGuard.JsonBody],
            EndpointRules.GuardsRequiredBy("DELETE", carriesABody: true));
    }

    [Fact]
    public void NoStateChangeIsLeftWithoutTheOrigin()
    {
        foreach (string method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            Assert.Contains(StateChangeGuard.Origin, EndpointRules.GuardsRequiredBy(method, carriesABody: false));
            Assert.Contains(StateChangeGuard.Origin, EndpointRules.GuardsRequiredBy(method, carriesABody: true));
        }
    }

    [Fact]
    public void TheTableIsAskedForAMethod()
    {
        Assert.Throws<ArgumentException>(() => EndpointRules.GuardsRequiredBy(" ", carriesABody: false));
    }
}
