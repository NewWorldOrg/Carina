using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class HybridcastAppDeliveryTests
{
    [Fact(DisplayName = "BR-BA-004: the frame opens the entry with the ticket alone, and is handed it with the receiver script put first into the head once")]
    public async Task TheFrameOpensTheEntryWithTheTicketAlone()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => new RelayedBody(
            RelayResult.Answered,
            200,
            "text/html; charset=utf-8",
            "<!DOCTYPE html><html><head><script src=\"/common/jquery.js\"></script></head><body>天気</body></html>"u8.ToArray(),
            address);

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        string page = await response.Content.ReadAsStringAsync();
        string relay = started.GetProperty("address").GetString()!.Split('?')[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(1, page.Split("data-hybridcast-relay").Length - 1);
        Assert.StartsWith($"<!DOCTYPE html><html><head><script src=\"{HybridcastRelayFeature.ReceiverScript}\" data-hybridcast-relay=\"{relay}\"", page, StringComparison.Ordinal);
        Assert.Contains($"<script src=\"{relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/common/jquery.js")}\"></script>", page, StringComparison.Ordinal);
        Assert.Contains("<body>天気</body>", page, StringComparison.Ordinal);
        Assert.Equal($"fetch 32736-1024 {HybridcastRelayFeature.Entry}", feature.Relay.Asked.Last());
    }

    [Theory(DisplayName = "BR-BA-004: the receiver script is put into a page loaded into the frame, and not into markup a script fetches, while every page is still pointed at the relay")]
    [InlineData("iframe", null, true)]
    [InlineData("document", null, true)]
    [InlineData("IFRAME", "navigate", true)]
    [InlineData("empty", null, false)]
    [InlineData("empty", "navigate", false)]
    [InlineData("script", "no-cors", false)]
    [InlineData(null, "navigate", true)]
    [InlineData(null, "cors", false)]
    [InlineData(null, "same-origin", false)]
    [InlineData(null, null, true)]
    public async Task TheReceiverScriptIsPutOnlyIntoAPageLoadedIntoTheFrame(string? destination, string? mode, bool injected)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => new RelayedBody(
            RelayResult.Answered,
            200,
            "text/html; charset=utf-8",
            "<div class=\"item\"><img src=\"img/a.png\"><object type=\"video/broadcast\"></object><script>var h = location.host;</script></div>"u8.ToArray(),
            address);
        using HttpRequestMessage asking = new(HttpMethod.Get, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/item-app.html"));

        if (destination is not null)
        {
            asking.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", destination);
        }

        if (mode is not null)
        {
            asking.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", mode);
        }

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);
        string page = await response.Content.ReadAsStringAsync();
        string relay = started.GetProperty("address").GetString()!.Split('?')[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(injected, page.Contains("data-hybridcast-relay", StringComparison.Ordinal));
        Assert.Equal(injected, !page.StartsWith("<div class=\"item\">", StringComparison.Ordinal));
        Assert.Contains($"<img src=\"{relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/p/img/a.png")}\">", page, StringComparison.Ordinal);
        Assert.Contains("<object data-hybridcast-type=\"video/broadcast\"></object>", page, StringComparison.Ordinal);
        Assert.Contains("<script>var h = __hcLocation.host;</script>", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: what is handed to the frame is sandboxed, never stored, and may reach nothing but this application's relay")]
    public async Task WhatIsHandedIsSandboxed()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string relay = started.GetProperty("address").GetString()!.Split('?')[0];

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        string policy = response.Headers.GetValues(HeaderNames.ContentSecurityPolicy).Single();

        Assert.StartsWith("sandbox allow-scripts allow-forms; default-src 'none';", policy, StringComparison.Ordinal);
        Assert.Contains($"connect-src http://localhost{relay} ws://localhost{relay.Replace("/app/", "/ws/", StringComparison.Ordinal)};", policy, StringComparison.Ordinal);
        Assert.Contains($"script-src 'unsafe-inline' 'unsafe-eval' blob: http://localhost{relay} http://localhost{HybridcastRelayFeature.ReceiverScript};", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues(HeaderNames.XContentTypeOptions).Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: a resource is handed over byte for byte with its type and status, as often as the frame asks while the grant lasts")]
    public async Task AResourceIsHandedOverByteForByte()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x3C, 0x68, 0x65, 0x61, 0x64, 0x3E];
        feature.Relay.Fetched = address => address.AbsolutePath == "/gone.png"
            ? new RelayedBody(RelayResult.Answered, 404, "text/plain", "gone"u8.ToArray(), address)
            : new RelayedBody(RelayResult.Answered, 200, "image/png", picture, address);

        using HttpResponseMessage entered = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        using HttpResponseMessage first = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://cdn.station.example.jp/a.png"), UriKind.Relative));
        using HttpResponseMessage gone = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://hc.station.example.jp/gone.png"), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal(picture, await first.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Theory(DisplayName = "BR-BA-004: an address outside the application's boundary is refused 403 and asked of nobody")]
    [InlineData("http://elsewhere.example/a.js")]
    [InlineData("https://station.example.jp/a.js")]
    [InlineData("http://10.0.0.1/a.js")]
    public async Task AnAddressOutsideTheBoundaryIsRefused(string address)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, address), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-013: a server that could not be reached is 502")]
    public async Task AServerThatCouldNotBeReachedIs502()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = _ => RelayedBody.Failed(RelayResult.TimedOut);

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("-3", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: an application's ticket opens its own channel's relay and no other")]
    public async Task AnApplicationsTicketOpensItsOwnRelayOnly()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage otherChannel = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1025/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(HybridcastRelayFeature.Entry)}", UriKind.Relative));
        using HttpResponseMessage unknown = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/{new string('A', 43)}?uri={Uri.EscapeDataString(HybridcastRelayFeature.Entry)}", UriKind.Relative));
        using HttpResponseMessage malformed = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/short?uri={Uri.EscapeDataString(HybridcastRelayFeature.Entry)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, otherChannel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BA-004: a ticket to watch a channel opens no application's relay")]
    public async Task ALiveTicketOpensNoApplicationsRelay()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        using HttpResponseMessage issued = await feature.Viewer.PostAsJsonAsync(new Uri("/api/live/ticket", UriKind.Relative), new { networkId = 32736, serviceId = 1024 });
        (_, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(issued);
        string ticket = body.GetProperty("data").GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(HybridcastRelayFeature.Entry)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BA-004: a script handed to the frame, and the scripts and handlers in its document, read the frame's stand-in for the location")]
    public async Task AScriptHandedToTheFrameReadsTheStandIn()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => address.AbsolutePath.EndsWith(".js", StringComparison.Ordinal)
            ? new RelayedBody(RelayResult.Answered, 200, "application/javascript; charset=utf-8", "if(location.hostname.indexOf(\"nhk\")>=0){go(\"location\")}"u8.ToArray(), address)
            : new RelayedBody(
                RelayResult.Answered,
                200,
                "text/html; charset=utf-8",
                "<html><head><script>var h = window.location.host;</script></head><body onload=\"location.hash='a'\"></body></html>"u8.ToArray(),
                address);

        using HttpResponseMessage script = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://hc.station.example.jp/p/index.js"), UriKind.Relative));
        using HttpResponseMessage page = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        string document = await page.Content.ReadAsStringAsync();

        Assert.Equal("if(__hcLocation.hostname.indexOf(\"nhk\")>=0){go(\"location\")}", await script.Content.ReadAsStringAsync());
        Assert.Equal("application/javascript; charset=utf-8", script.Content.Headers.ContentType?.ToString());
        Assert.Contains("<script>var h = window.__hcLocation.host;</script>", document, StringComparison.Ordinal);
        Assert.Contains("<body onload=\"__hcLocation.hash='a'\">", document, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: a script handed to the frame in Shift_JIS has its second bytes kept apart from the code")]
    public async Task AShiftJisScriptKeepsItsSecondBytesApart()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        byte[] script = [.. "var a=\""u8, 0x95, 0x5C, .. "\",b=\"location\";location.href"u8];
        feature.Relay.Fetched = address => new RelayedBody(RelayResult.Answered, 200, "text/javascript; charset=Shift_JIS", script, address);

        using HttpResponseMessage handed = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://hc.station.example.jp/p/index.js"), UriKind.Relative));

        Assert.Equal(
            [.. "var a=\""u8, 0x95, 0x5C, .. "\",b=\"location\";__hcLocation.href"u8],
            await handed.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "BR-BA-004: a script library's request from the frame is answered with the type the server named, and nothing of its headers is asked of the relay")]
    public async Task AScriptLibrarysRequestIsAnsweredWithTheServersType()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => new RelayedBody(RelayResult.Answered, 200, "application/json; charset=utf-8", "{\"a\":1}"u8.ToArray(), address);
        using HttpRequestMessage asking = new(HttpMethod.Get, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/list.json"));
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");
        asking.Headers.TryAddWithoutValidation(HeaderNames.XRequestedWith, "XMLHttpRequest");
        asking.Headers.TryAddWithoutValidation(HeaderNames.Accept, "application/json, text/javascript, */*; q=0.01");

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("{\"a\":1}", await response.Content.ReadAsStringAsync());
        Assert.Equal("*", response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());
        Assert.Equal("fetch 32736-1024 http://hc.station.example.jp/p/list.json", feature.Relay.Asked.Last());
    }

    [Theory(DisplayName = "BR-BA-004: a preflight from the frame is answered for GET and POST, the safe request headers and the content type, and asks nothing of the server")]
    [InlineData("POST", "content-type")]
    [InlineData("GET", "accept,accept-language,content-language")]
    [InlineData("GET", "x-requested-with")]
    [InlineData("POST", "content-type,x-requested-with")]
    public async Task APreflightFromTheFrameIsAnswered(string method, string headers)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        int askedBefore = feature.Relay.Asked.Count;

        using HttpResponseMessage response = await feature.Frame.SendAsync(HybridcastRelayFeature.Preflight(HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote"), method, headers));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());
        Assert.Contains(method, response.Headers.GetValues(HeaderNames.AccessControlAllowMethods).Single(), StringComparison.Ordinal);
        Assert.All(
            headers.Split(','),
            header => Assert.Contains(header, response.Headers.GetValues(HeaderNames.AccessControlAllowHeaders).Single(), StringComparison.OrdinalIgnoreCase));
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials));
        Assert.Equal(askedBefore, feature.Relay.Asked.Count);
    }

    [Theory(DisplayName = "BR-BA-004: a preflight asking for another method or for a header beyond the safe ones and the content type is not allowed it")]
    [InlineData("PUT", "", "PUT")]
    [InlineData("DELETE", "", "DELETE")]
    [InlineData("POST", "x-custom", "x-custom")]
    [InlineData("POST", "authorization", "authorization")]
    [InlineData("POST", "content-type,cookie", "cookie")]
    public async Task APreflightAskingForMoreIsNotAllowedIt(string method, string headers, string refused)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();

        using HttpResponseMessage response = await feature.Frame.SendAsync(HybridcastRelayFeature.Preflight(HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote"), method, headers));
        string[] allowed =
        [
            .. new[] { HeaderNames.AccessControlAllowMethods, HeaderNames.AccessControlAllowHeaders }
                .SelectMany(name => response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values : [])
                .SelectMany(value => value.Split(','))
                .Select(value => value.Trim()),
        ];

        Assert.DoesNotContain(refused, allowed, StringComparer.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials));
    }

    [Fact(DisplayName = "BR-BD-011: the frame posts through its relay from an origin of its own with no cookie either way, and the answer is handed back as a fetch's is")]
    public async Task TheFramePostsThroughItsRelay()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => new RelayedBody(RelayResult.Answered, 200, "text/html; charset=utf-8", "<html><head></head><body>済</body></html>"u8.ToArray(), address);
        using HttpRequestMessage asking = new(HttpMethod.Post, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote"))
        {
            Content = new StringContent("choice=2", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");
        asking.Headers.TryAddWithoutValidation(HeaderNames.Cookie, "carina_session=held");

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);
        string page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());
        Assert.StartsWith("sandbox allow-scripts allow-forms;", response.Headers.GetValues(HeaderNames.ContentSecurityPolicy).Single(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
        Assert.Contains("data-hybridcast-relay", page, StringComparison.Ordinal);
        Assert.Equal(
            "post 32736-1024 http://hc.station.example.jp/p/vote application/x-www-form-urlencoded; charset=utf-8 choice=2",
            feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BD-011: a post that names no type is relayed with none")]
    public async Task APostThatNamesNoTypeIsRelayedWithNone()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        using HttpRequestMessage asking = new(HttpMethod.Post, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/beacon"));

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("post 32736-1024 http://hc.station.example.jp/p/beacon untyped ", feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BD-011: a posted body is read no further than one byte past the most the relay sends")]
    public async Task APostedBodyIsReadNoFurtherThanTheRelaySends()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        using HttpRequestMessage asking = new(HttpMethod.Post, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote"))
        {
            Content = new ByteArrayContent(new byte[1024 * 1024]),
        };

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal(HybridcastRelaySettings.Default.LargestSent + 1, Assert.Single(feature.Relay.Posts).Bytes.Length);
    }

    [Theory(DisplayName = "BR-BA-004: a post outside the application's boundary, or with a ticket that started nothing, is refused and asked of nobody")]
    [InlineData("http://elsewhere.example/p/vote", false)]
    [InlineData("http://hc.station.example.jp/p/vote", true)]
    public async Task APostOutsideIsRefused(string address, bool anotherTicket)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string relayed = HybridcastRelayFeature.Relayed(started, "app", address).OriginalString;
        Uri target = anotherTicket ? new Uri(relayed.Replace(started.GetProperty("inTheClear").GetString()!, new string('A', 43), StringComparison.Ordinal), UriKind.Relative) : new Uri(relayed, UriKind.Relative);
        int askedBefore = feature.Relay.Asked.Count;
        using HttpRequestMessage asking = new(HttpMethod.Post, target) { Content = new StringContent("a=1") };
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(askedBefore, feature.Relay.Asked.Count);
    }

    [Fact(DisplayName = "no log line names the ticket an application's frame carries, however finely the logs are turned up")]
    public async Task NoLogLineNamesTheTicket()
    {
        RecordedStartup heard = new();
        await using HybridcastRelayFeature feature = new(heard);
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage entered = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        using HttpResponseMessage outside = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://elsewhere.example/a.js"), UriKind.Relative));
        using HttpResponseMessage preflight = await feature.Frame.SendAsync(HybridcastRelayFeature.Preflight(HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote"), "POST", "content-type"));
        using HttpRequestMessage posting = new(HttpMethod.Post, HybridcastRelayFeature.Relayed(started, "app", "http://hc.station.example.jp/p/vote")) { Content = new StringContent("a=1") };
        posting.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");
        using HttpResponseMessage posted = await feature.Frame.SendAsync(posting);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.NotEmpty(heard.Everything);
        Assert.DoesNotContain(heard.Everything, line => line.Text.Contains(ticket, StringComparison.Ordinal)
            || line.Values.Any(value => value.Value?.ToString()?.Contains(ticket, StringComparison.Ordinal) is true));
    }
}
