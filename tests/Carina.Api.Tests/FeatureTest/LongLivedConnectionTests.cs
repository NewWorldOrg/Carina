using System.Net;

using Carina.Api.Events;
using Carina.Contracts;
using Carina.Domain.Auth;
using Carina.Infrastructure.Events;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LongLivedConnectionTests
{
    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    [Fact]
    public async Task AStreamIsOpenedForACallerCarryingASessionCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AStreamIsRefusedToACallerCarryingNoCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AStreamThatIsAlreadyOpenKeepsCarryingSignalsAfterItsSessionIsEnded()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession session = await probe.SignedInAsync();
        AppEventHub hub = probe.Wired.Services.GetRequiredService<AppEventHub>();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        session.Revoke(DateTime.UtcNow);

        hub.Signal(AppEventName.Tuners);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("event: tuners", await reader.ReadLineAsync());
    }

    [Fact]
    public async Task TheNextConnectionAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
