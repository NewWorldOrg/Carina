using System.Net;
using System.Net.Http.Headers;

using Carina.Api.Events;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class EventStreamRefusalTests
{
    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    [Fact]
    public async Task AListenerCarryingNoCookieIsRefusedRatherThanHandedAStreamThatCarriesTheRefusal()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        probe.Client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(AppEventStream.ContentType));

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AStreamOpenedForACallerIsMarkedSoNothingInFrontHandsItToTheNextCaller()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }
}
