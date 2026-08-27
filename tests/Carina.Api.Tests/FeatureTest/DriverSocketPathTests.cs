using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DriverSocketPathTests
{
    public static readonly IReadOnlyList<string> NotAskedBecauseTheAnswerNeverEnds =
    [
        "/api/events",
        "/api/programs/bulk",
    ];

    [Fact]
    public async Task NoSurfaceOfThisApplicationNamesWhereTheDriverSocketIs()
    {
        string wherever = SomewhereTheRuntimeWillTalkAbout();
        await using var factory = new TestingWebApplicationFactory { DriverSocketPath = wherever };
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = Authenticated(guarded);

        IReadOnlyList<AnsweredSurface> answered = await EverySurfaceAsync(guarded, client);

        Assert.Empty(DriverSocketPathLeak.In(answered, wherever));
    }

    [Fact]
    public async Task TheSweepGoesAsFarAsTheDriverOnSurfacesTheOperatingSystemRefuses()
    {
        string absent = Path.Combine(
            Directory.CreateTempSubdirectory("carina-socket-of-this-host-").FullName,
            "driver.sock");
        SocketError said = await WhatTheSocketSaysAsync(absent);

        await using var factory = new TestingWebApplicationFactory { DriverSocketPath = absent };
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = Authenticated(guarded);

        IReadOnlyList<AnsweredSurface> answered = await EverySurfaceAsync(guarded, client);
        AnsweredSurface[] wentAsFarAsTheSocket =
        [
            .. answered.Where(answer => answer.Body.Contains(said.ToString(), StringComparison.Ordinal)),
        ];

        Assert.True(
            wentAsFarAsTheSocket.Length >= 4,
            $"only {wentAsFarAsTheSocket.Length} surface(s) reached the socket, so the sweep proves little: "
            + string.Join(" | ", answered.Select(answer => $"{answer.Surface} {answer.Status}")));
        Assert.All(
            wentAsFarAsTheSocket,
            answer => Assert.Equal(StatusCodes.Status503ServiceUnavailable, answer.Status));
        Assert.Empty(DriverSocketPathLeak.In(answered, absent));
        Assert.True(
            answered.Count > NotAskedBecauseTheAnswerNeverEnds.Count,
            "the sweep asked fewer surfaces than it skipped");
    }

    private static async Task<SocketError> WhatTheSocketSaysAsync(string socketPath)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException refused)
        {
            return refused.SocketErrorCode;
        }

        throw new InvalidOperationException($"Something is listening at {socketPath}, so nothing was refused.");
    }

    [Fact]
    public async Task TheSurfacesTheSweepSkipsAreTheOnesWhoseAnswerNeverEnds()
    {
        await using var factory = new TestingWebApplicationFactory();

        string[] routed = [.. RouteInventory.Of(factory).Select(surface => surface.Pattern)];

        Assert.Equal(
            ["/api/events", "/api/programs/bulk"],
            NotAskedBecauseTheAnswerNeverEnds.Order(StringComparer.Ordinal).ToArray());
        Assert.All(
            NotAskedBecauseTheAnswerNeverEnds,
            skipped => Assert.Contains(skipped, routed, StringComparer.Ordinal));
    }

    private static string SomewhereTheRuntimeWillTalkAbout()
        => Path.Combine(
            Directory.CreateTempSubdirectory("carina-socket-of-this-host-").FullName,
            new string('x', 200) + ".sock");

    private static HttpClient Authenticated(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        client.Timeout = TimeSpan.FromSeconds(20);

        return client;
    }

    private static async Task<IReadOnlyList<AnsweredSurface>> EverySurfaceAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client)
    {
        var answered = new List<AnsweredSurface>();

        foreach (RoutedSurface surface in RouteInventory.Of(factory))
        {
            string path = RouteInventory.SamplePath(surface.Pattern);

            if (NotAskedBecauseTheAnswerNeverEnds.Contains(surface.Pattern, StringComparer.Ordinal))
            {
                continue;
            }

            using var asking = new HttpRequestMessage(
                new HttpMethod(surface.Method),
                new Uri(path, UriKind.Relative));

            if (!HttpMethods.IsGet(surface.Method) && !HttpMethods.IsHead(surface.Method))
            {
                asking.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await client.SendAsync(asking);

            answered.Add(new AnsweredSurface(
                surface.ToString(),
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync()));
        }

        return answered;
    }
}
