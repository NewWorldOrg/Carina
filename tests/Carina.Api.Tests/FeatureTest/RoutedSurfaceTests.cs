using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Events;
using Carina.Api.Live;
using Carina.Api.Playback;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RoutedSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const int TheSurfacesThisRepositoryHadWhenTheSweepWasWritten = 50;

    private const string MigrationRoot = "/api/migration";

    public static readonly IReadOnlyList<string> StreamingRoots = ["/api/live", "/api/videos"];

    private static readonly string[] SurfacesTheFeatureHadWhenTheseRulesWereWritten =
    [
        "GET /api/live/channels",
        "GET /api/live/profiles",
        "GET /api/live/sessions",
        "GET /api/live/ws",
        "POST /api/live/ticket",
        "GET /api/videos/{id}",
        "HEAD /api/videos/{id}",
        "GET /api/videos/{id}/play",
        "PUT /api/videos/{id}/position",
        "GET /api/videos/{id}/scrub",
        "GET /api/videos/{id}/thumbnail",
        "POST /api/videos/{id}/ticket",
    ];

    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<string> NotAskedBecauseTheAnswerNeverEnds =
    [
        "/api/events",
        "/api/programs/bulk",
    ];

    private readonly PipedSupply supply = new();

    [Fact]
    public void EveryRoutedSurfaceSaysWhetherItDestroys()
    {
        Assert.Empty(EndpointRules.SurfacesWithoutADeclaredEffect(Inventory()));
    }

    [Fact]
    public void NoRoutedSurfaceChangesStateOnAMethodThatOnlyReads()
    {
        Assert.Empty(EndpointRules.SurfacesChangingStateWithoutAskingForIt(Inventory()));
    }

    [Fact(DisplayName = "BR-BA-004: the Hybridcast frame's two relays are the only surfaces open to another origin, and they only read")]
    public void TheFramesRelaysAreTheOnlySurfacesOpenToAnotherOrigin()
    {
        RoutedSurface[] open = [.. Inventory().Where(surface => surface.OpenToTheFrame).OrderBy(surface => surface.ToString(), StringComparer.Ordinal)];

        string ticket = $"{{ticket:length({Unguessable.Length})}}";

        Assert.Equal(
            [
                $"GET /api/live/{{networkId:int}}-{{serviceId:int}}/data-broadcast/app/{ticket}",
                $"GET /api/live/{{networkId:int}}-{{serviceId:int}}/data-broadcast/ws/{ticket}",
                $"POST /api/live/{{networkId:int}}-{{serviceId:int}}/data-broadcast/app/{ticket}",
            ],
            open.Select(surface => surface.ToString()).ToArray());
        Assert.All(open, surface => Assert.Equal(EndpointEffect.Reading, surface.Effect));
    }

    [Fact]
    public void TheDestructiveSurfacesAreTheOnesThatDiscardWhatCannotBeCollectedAgain()
    {
        Assert.Equal(
            [
                "DELETE /api/auth/sessions/{id}",
                "DELETE /api/encoding/destinations/{id:guid}",
                "DELETE /api/encoding/profiles/{id:guid}",
                "DELETE /api/notifications/destinations/{id:guid}",
                "DELETE /api/notifications/rules/{id:guid}",
                "DELETE /api/recordings/{id}",
                "DELETE /api/reservations/{id:guid}",
                "DELETE /api/rules/{id:guid}",
                "DELETE /api/services/{networkId:int}-{serviceId:int}/candidate-channels/{candidateChannelId:guid}",
                "POST /api/auth/password",
                "POST /api/epg/archive/forget-service",
                "POST /api/epg/rebuild",
                "POST /api/recordings/integrity/findings/{findingId}/delete",
                "POST /api/recordings/{id}/stop",
                "POST /api/tuners/scan/{scanId:guid}/apply",
                "PUT /api/auth/oidc-config",
            ],
            Inventory()
                .Where(surface => surface.Effect is EndpointEffect.Destructive)
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheSurfacesThatDeleteAreTheOnesTheLedgerNames()
    {
        Assert.Equal(
            [
                "DELETE /api/auth/sessions/{id}",
                "DELETE /api/encoding/destinations/{id:guid}",
                "DELETE /api/encoding/profiles/{id:guid}",
                "DELETE /api/notifications/destinations/{id:guid}",
                "DELETE /api/notifications/rules/{id:guid}",
                "DELETE /api/notifications/watches/{id:guid}",
                "DELETE /api/recordings/{id}",
                "DELETE /api/reservations/{id:guid}",
                "DELETE /api/rules/{id:guid}",
                "DELETE /api/services/{networkId:int}-{serviceId:int}/candidate-channels/{candidateChannelId:guid}",
            ],
            EndpointRules.SurfacesThatDelete(Inventory()));
    }

    [Fact]
    public void TheOnlyWayToDeleteUnderTheRecordingSurfaceIsTheOneAPersonAsksForByHand()
    {
        Assert.Equal(
            ["DELETE /api/recordings/{id}"],
            EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/recordings"));
    }

    [Fact]
    public void TheOneWayToDeleteARecordingSaysItDestroys()
    {
        Assert.Equal(
            EndpointEffect.Destructive,
            Inventory()
                .Single(surface => surface.ToString() == "DELETE /api/recordings/{id}")
                .Effect);
    }

    [Fact]
    public void NothingUnderTheIntegritySurfaceDeletesARowOrTakesTheDeleteMethod()
    {
        Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/recordings/integrity"));
        Assert.NotEmpty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/recordings"));
    }

    [Fact]
    public void TheOneWayUnderTheIntegritySurfaceThatThrowsAFileAwayNamesAFindingAndSaysItDestroys()
    {
        Assert.Equal(
            ["POST /api/recordings/integrity/findings/{findingId}/delete"],
            Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/recordings/integrity", StringComparison.Ordinal))
                .Where(surface => surface.Effect is EndpointEffect.Destructive)
                .Select(surface => surface.ToString())
                .ToArray());
    }

    [Fact]
    public void TheEncodingSurfacesAreTheSixteenTheDefinitionsTheQueueAndTheAutoRunAreReachedThrough()
    {
        Assert.Equal(
            [
                "DELETE /api/encoding/destinations/{id:guid}",
                "DELETE /api/encoding/profiles/{id:guid}",
                "GET /api/encoding/destinations",
                "GET /api/encoding/jobs",
                "GET /api/encoding/jobs/durations",
                "GET /api/encoding/profiles",
                "GET /api/encoding/profiles/defaults",
                "GET /api/encoding/settings",
                "PATCH /api/encoding/destinations/{id:guid}",
                "PATCH /api/encoding/profiles/{id:guid}",
                "POST /api/encoding/destinations",
                "POST /api/encoding/jobs",
                "POST /api/encoding/jobs/retry",
                "POST /api/encoding/jobs/{id:guid}/cancel",
                "POST /api/encoding/profiles",
                "PUT /api/encoding/settings",
            ],
            Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/encoding", StringComparison.Ordinal))
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheOnlyWayToDeleteUnderTheEncodingSurfaceIsTheTwoThatTakeADefinitionOutOfUse()
    {
        Assert.Equal(
            [
                "DELETE /api/encoding/destinations/{id:guid}",
                "DELETE /api/encoding/profiles/{id:guid}",
            ],
            EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/encoding"));
    }

    [Fact]
    public void NothingUnderTheEncodingSurfaceReachesAnArtefactOrAJobRow()
    {
        Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/encoding/jobs"));
    }

    [Fact]
    public void TheTwoWaysToTakeAnEncodeDefinitionOutOfUseSayTheyDestroy()
    {
        Assert.Equal(
            [EndpointEffect.Destructive, EndpointEffect.Destructive],
            Inventory()
                .Where(surface => EndpointRules
                    .SurfacesThatDeleteUnder(Inventory(), "/api/encoding")
                    .Contains(surface.ToString(), StringComparer.Ordinal))
                .Select(surface => surface.Effect)
                .ToArray());
    }

    [Fact]
    public void ChangingAnEncodeDefinitionDiscardsNothing()
    {
        Assert.Equal(
            [EndpointEffect.Changing, EndpointEffect.Changing],
            Inventory()
                .Where(surface => surface.Method == HttpMethods.Patch
                    && surface.Pattern.StartsWith("/api/encoding", StringComparison.Ordinal))
                .OrderBy(surface => surface.Pattern, StringComparer.Ordinal)
                .Select(surface => surface.Effect)
                .ToArray());
    }

    [Fact(DisplayName = "BR-NA-002: the notification surfaces are the destinations and the sets of rules listed, made, changed and removed, a destination tried, the deliveries listed, the settings read and changed, the programmes asked to be told of listed, asked for and withdrawn, and the key notices to a browser are signed with read and a browser registered and given another subscription, and removing a destination or a set of rules discards")]
    public void TheNotificationSurfacesAreTheOnesTheDestinationsRulesDeliveriesAndSettingsAreReachedThrough()
    {
        RoutedSurface[] notifications =
        [
            .. Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/notifications", StringComparison.Ordinal))
                .OrderBy(surface => surface.ToString(), StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "DELETE /api/notifications/destinations/{id:guid} Destructive",
                "DELETE /api/notifications/rules/{id:guid} Destructive",
                "DELETE /api/notifications/watches/{id:guid} Changing",
                "GET /api/notifications/deliveries Reading",
                "GET /api/notifications/destinations Reading",
                "GET /api/notifications/push/key Reading",
                "GET /api/notifications/rules Reading",
                "GET /api/notifications/settings Reading",
                "GET /api/notifications/watches Reading",
                "PATCH /api/notifications/destinations/{id:guid} Changing",
                "PATCH /api/notifications/rules/{id:guid} Changing",
                "POST /api/notifications/destinations Changing",
                "POST /api/notifications/destinations/{id:guid}/test Changing",
                "POST /api/notifications/push/subscriptions Changing",
                "POST /api/notifications/rules Changing",
                "POST /api/notifications/watches Changing",
                "PUT /api/notifications/push/subscriptions/{destinationId:guid} Changing",
                "PUT /api/notifications/settings Changing",
            ],
            notifications.Select(surface => $"{surface} {surface.Effect}").ToArray());
    }

    [Fact(DisplayName = "the segment surfaces are the settings read and changed, the status read and the version in use chosen, and none of them discards anything")]
    public void TheSegmentSurfacesAreTheSettingsTheStatusAndTheVersion()
    {
        RoutedSurface[] segments =
        [
            .. Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/segments", StringComparison.Ordinal))
                .OrderBy(surface => surface.ToString(), StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["GET /api/segments/settings", "GET /api/segments/status", "PATCH /api/segments/settings", "PATCH /api/segments/version"],
            segments.Select(surface => surface.ToString()).ToArray());
        Assert.Equal(
            [EndpointEffect.Reading, EndpointEffect.Reading, EndpointEffect.Changing, EndpointEffect.Changing],
            segments.Select(surface => surface.Effect).ToArray());
    }

    [Fact(DisplayName = "BR-CA-001: the stretches of a recording are read under the recording and the skip events taken there, the one write a change and none destructive, and nothing there takes an answer")]
    public void TheStretchesOfARecordingAreReadUnderTheRecording()
    {
        RoutedSurface[] segments =
        [
            .. Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/recordings/{id}/segments", StringComparison.Ordinal)
                                  || surface.Pattern.StartsWith("/api/recordings/{id}/skips", StringComparison.Ordinal))
                .OrderBy(surface => surface.ToString(), StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["GET /api/recordings/{id}/segments", "POST /api/recordings/{id}/skips"],
            segments.Select(surface => surface.ToString()).ToArray());
        Assert.Equal([EndpointEffect.Reading, EndpointEffect.Changing], segments.Select(surface => surface.Effect).ToArray());
    }

    [Fact(DisplayName = "BR-BD-016: the data broadcast settings are read and changed through two surfaces, and changing them discards nothing")]
    public void TheDataBroadcastSettingsSurfacesAreTheTwoTheyAreReadAndChangedThrough()
    {
        RoutedSurface[] settings =
        [
            .. Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/data-broadcast", StringComparison.Ordinal))
                .OrderBy(surface => surface.ToString(), StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["GET /api/data-broadcast/settings", "PUT /api/data-broadcast/settings"],
            settings.Select(surface => surface.ToString()).ToArray());
        Assert.Equal(
            [EndpointEffect.Reading, EndpointEffect.Changing],
            settings.Select(surface => surface.Effect).ToArray());
    }

    [Fact]
    public void TheReservationSurfacesAreTheNineAReservationIsMadeChangedThrownAwayAndAccountedForThrough()
    {
        Assert.Equal(
            [
                "DELETE /api/reservations/{id:guid}",
                "GET /api/reservations",
                "GET /api/reservations/health",
                "GET /api/reservations/outcomes",
                "GET /api/reservations/{id:guid}",
                "PATCH /api/reservations/{id:guid}",
                "POST /api/reservations",
                "POST /api/reservations/{id:guid}/cancel",
                "POST /api/reservations/{id:guid}/restore",
            ],
            Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/reservations", StringComparison.Ordinal))
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheOnlyWayToDeleteUnderTheReservationSurfaceIsTheOneAPersonAsksForByHand()
    {
        Assert.Equal(
            ["DELETE /api/reservations/{id:guid}"],
            EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/reservations"));
    }

    [Fact]
    public void TheOneWayToDeleteAReservationSaysItDestroys()
    {
        Assert.Equal(
            EndpointEffect.Destructive,
            Inventory()
                .Single(surface => surface.ToString() == "DELETE /api/reservations/{id:guid}")
                .Effect);
    }

    [Fact]
    public void CancellingAReservationDiscardsNothing()
    {
        Assert.Equal(
            EndpointEffect.Changing,
            Inventory()
                .Single(surface => surface.ToString() == "POST /api/reservations/{id:guid}/cancel")
                .Effect);
    }

    [Fact]
    public void TheRuleSurfacesAreTheNineARuleIsWrittenChangedAppliedAndThrownAwayThrough()
    {
        Assert.Equal(
            [
                "DELETE /api/rules/{id:guid}",
                "GET /api/rules",
                "GET /api/rules/{id:guid}",
                "PATCH /api/rules/{id:guid}/enabled",
                "POST /api/rules",
                "POST /api/rules/impact",
                "POST /api/rules/preview",
                "POST /api/rules/{id:guid}/apply-now",
                "PUT /api/rules/{id:guid}",
            ],
            Inventory()
                .Where(surface => surface.Pattern.StartsWith("/api/rules", StringComparison.Ordinal))
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheOnlyWayToDeleteUnderTheRuleSurfaceIsTheOneAPersonAsksForByHand()
    {
        Assert.Equal(
            ["DELETE /api/rules/{id:guid}"],
            EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/rules"));
    }

    [Fact]
    public void TheOneWayToDeleteARuleSaysItDestroys()
    {
        Assert.Equal(
            EndpointEffect.Destructive,
            Inventory()
                .Single(surface => surface.ToString() == "DELETE /api/rules/{id:guid}")
                .Effect);
    }

    [Fact]
    public void RehearsingARuleChangesNothing()
    {
        Assert.Equal(
            [EndpointEffect.Reading, EndpointEffect.Reading],
            Inventory()
                .Where(surface => surface.Pattern is "/api/rules/preview" or "/api/rules/impact")
                .OrderBy(surface => surface.Pattern, StringComparer.Ordinal)
                .Select(surface => surface.Effect)
                .ToArray());
    }

    [Fact]
    public void TheSurfacesThatCheckTheLedgerAgainstTheFilesAreTheThreeThatWereAskedFor()
    {
        Assert.Equal(
            [
                "GET /api/recordings/integrity",
                "GET /api/storage",
                "POST /api/recordings/integrity/run",
            ],
            Inventory()
                .Where(surface => surface.Pattern
                    is "/api/recordings/integrity"
                    or "/api/recordings/integrity/run"
                    or "/api/storage")
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void AskingForTheLedgerToBeCheckedDiscardsNothing()
    {
        Assert.Equal(
            EndpointEffect.Changing,
            Inventory()
                .Single(surface => surface.ToString() == "POST /api/recordings/integrity/run")
                .Effect);
    }

    [Fact]
    public void TheMigrationSurfaceIsTheOneTheRecordIsReadThrough()
        => Assert.Equal(
            ["GET /api/migration/record"],
            MigrationSurfaces().Select(surface => surface.ToString()).Order(StringComparer.Ordinal).ToArray());

    [Fact]
    public void NothingAnywhereElseOffersAMigrationOfItsOwn()
        => Assert.Equal(
            ["/api/migration/record"],
            Inventory()
                .Select(surface => surface.Pattern)
                .Where(pattern => pattern.Contains("migration", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    [Fact]
    public void NothingUnderTheMigrationSurfaceIsAskedForOnAMethodThatWrites()
        => Assert.All(MigrationSurfaces(), surface => Assert.True(
            HttpMethods.IsGet(surface.Method),
            $"{surface} reaches the record on a method that is not a read"));

    [Fact]
    public void NothingUnderTheMigrationSurfaceChangesAnythingOrSaysItDestroys()
        => Assert.All(MigrationSurfaces(), surface => Assert.Equal(EndpointEffect.Reading, surface.Effect));

    [Fact]
    public void NothingUnderTheMigrationSurfaceDeletes()
        => Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), MigrationRoot));

    [Fact]
    public void TheRootIsReadAsAWholeSegmentAndNotAsLeadingLetters()
    {
        RoutedSurface[] inventory =
        [
            new("POST", "/api/migrations-of-a-kind/run", EndpointEffect.Changing),
            new("POST", "/api/migration/run", EndpointEffect.Changing),
        ];

        Assert.Equal(
            ["POST /api/migration/run"],
            inventory.Where(UnderTheMigrationRoot).Select(surface => surface.ToString()).ToArray());
    }

    [Fact]
    public void TheInventoryHoldsTheSurfacesTheDocumentCannotDescribe()
    {
        string[] patterns = [.. Inventory().Select(surface => surface.Pattern)];

        Assert.Contains("/api/events", patterns, StringComparer.Ordinal);
        Assert.Contains("/api/programs/bulk", patterns, StringComparer.Ordinal);
        Assert.Contains(LiveWire.Path, patterns, StringComparer.Ordinal);
        Assert.Contains(LiveStreamDelivery.Path, patterns, StringComparer.Ordinal);
        Assert.Contains(VideoDelivery.Path, patterns, StringComparer.Ordinal);
        Assert.Contains(VideoDelivery.WithTheTicketInThePath, patterns, StringComparer.Ordinal);
        Assert.Contains(LiveStreamDelivery.WithTheTicketInThePath, patterns, StringComparer.Ordinal);
        Assert.Contains(LiveHlsDelivery.ThePlaylistOfAKey, patterns, StringComparer.Ordinal);
        Assert.Contains(LiveHlsDelivery.TheInitialisationOfAKey, patterns, StringComparer.Ordinal);
        Assert.Contains(LiveHlsDelivery.ASegmentOfAKey, patterns, StringComparer.Ordinal);
        Assert.Contains("/api/health", patterns, StringComparer.Ordinal);
        Assert.Contains(OidcHandshake.StartPath, patterns, StringComparer.Ordinal);
        Assert.Contains(OidcHandshake.CallbackPath, patterns, StringComparer.Ordinal);
    }

    [Fact]
    public async Task EveryRoutedSurfaceOutsideTheEnumeratedListRefusesAClientCarryingNoCredentials()
    {
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = guarded.CreateClient();
        var admitted = new List<string>();
        int asked = 0;

        foreach (RoutedSurface surface in RouteInventory.Of(guarded))
        {
            string path = RouteInventory.SamplePath(surface.Pattern);

            if (AnonymousSurfaces.WhileDeveloping.Admit(surface.Method, path))
            {
                continue;
            }

            using var asking = new HttpRequestMessage(
                new HttpMethod(surface.Method),
                new Uri(path, UriKind.Relative));
            using HttpResponseMessage response = await client.SendAsync(
                asking,
                HttpCompletionOption.ResponseHeadersRead);

            asked++;

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                admitted.Add($"{surface.Method} {path} answered {(int)response.StatusCode}");

                continue;
            }

            if ((await response.Content.ReadAsByteArrayAsync()).Length > 0)
            {
                admitted.Add($"{surface.Method} {path} refused carrying a body");
            }
        }

        Assert.Empty(admitted);
        Assert.True(
            asked >= TheSurfacesThisRepositoryHadWhenTheSweepWasWritten,
            $"the sweep asked {asked} surfaces, which is fewer than it was written against");
    }

    [Fact]
    public async Task TheEnumeratedSurfacesAreReachedWithoutCredentialsWhileASchemeIsRegistered()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage health = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        using HttpResponseMessage document = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/sample.json")]
    [InlineData("/api/health/detail")]
    [InlineData("/_next/staticbait/main.js")]
    public async Task APathThatMerelyLooksLikeAnEnumeratedSurfaceIsRefused(string path)
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TheStreamingSurfacesAreInTheInventoryTheEffectRuleReads()
    {
        string[] streaming = [.. Streaming().Select(surface => surface.ToString())];

        Assert.All(
            SurfacesTheFeatureHadWhenTheseRulesWereWritten,
            surface => Assert.Contains(surface, streaming, StringComparer.Ordinal));
    }

    [Fact]
    public void NothingUnderTheStreamingSurfaceDeletes()
    {
        Assert.All(StreamingRoots, root => Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), root)));
    }

    [Fact]
    public void NothingUnderTheStreamingSurfaceSaysItDestroys()
    {
        Assert.Empty(
            Streaming()
                .Where(surface => surface.Effect is EndpointEffect.Destructive)
                .Select(surface => surface.ToString()));
    }

    [Fact]
    public void TheOnlyThingsThatChangeStateUnderTheStreamingSurfaceAreATicketAndAPositionBeingKept()
    {
        RoutedSurface[] changing =
        [
            .. Streaming().Where(surface => surface.Effect is EndpointEffect.Changing),
        ];

        Assert.NotEmpty(changing);
        Assert.All(
            changing,
            surface =>
            {
                if (surface.Pattern.EndsWith("/position", StringComparison.Ordinal))
                {
                    Assert.True(
                        HttpMethods.IsPut(surface.Method),
                        $"{surface} keeps where the watching got to on {surface.Method}");

                    return;
                }

                Assert.True(HttpMethods.IsPost(surface.Method), $"{surface} changes state on {surface.Method}");
                Assert.EndsWith("/ticket", surface.Pattern, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void TheRootsAreReadAsWholeSegmentsAndNotAsLeadingLetters()
    {
        RoutedSurface[] inventory =
        [
            new("DELETE", "/api/videos-archive/{id}", EndpointEffect.Destructive),
            new("DELETE", "/api/livestock/{id}", EndpointEffect.Destructive),
            new("DELETE", "/api/live/sessions/{id}", EndpointEffect.Destructive),
        ];

        Assert.Equal(
            ["DELETE /api/live/sessions/{id}"],
            StreamingRoots.SelectMany(root => EndpointRules.SurfacesThatDeleteUnder(inventory, root)).ToArray());
        Assert.Equal(
            ["DELETE /api/live/sessions/{id}"],
            inventory.Where(UnderARoot).Select(surface => surface.ToString()).ToArray());
    }

    [Theory]
    [InlineData("GET", "/api/live/channels")]
    [InlineData("GET", "/api/live/profiles")]
    [InlineData("GET", "/api/live/sessions")]
    [InlineData("GET", "/api/live/departures")]
    [InlineData("POST", "/api/live/ticket")]
    public async Task EveryLiveSurfaceRefusesACallerCarryingNoCredentialsWithoutABodyAndWithoutRedirecting(string method, string path)
    {
        await using LiveFeature feature = new();

        using HttpRequestMessage asking = new(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (method is "POST")
        {
            asking.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await feature.Anonymous.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/api/live/ws")]
    [InlineData("/api/live/sessions")]
    [InlineData("/api/live/channels")]
    [InlineData("/api/live/profiles")]
    [InlineData("/api/live/departures")]
    [InlineData("/api/live/32736-1024/data-broadcast/fetch?uri=http%3A%2F%2Fbc.example.jp%2F")]
    [InlineData("/api/live/32736-1024/data-broadcast/receiver-id")]
    [InlineData("/api/live/32736-1024/data-broadcast/ait?uri=http%3A%2F%2Fbc.example.jp%2Fapp.ait")]
    [InlineData(AppEventStream.Path)]
    public async Task ALiveTicketOpensNoOtherSurfaceABrowserReachesWithItsCookie(string path)
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage response = await player.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "BR-BA-004: the frame's two relays answer any origin, without credentials and naming no header beyond the safe ones")]
    [InlineData("app")]
    [InlineData("ws")]
    public async Task TheFramesRelaysAnswerAnyOrigin(string relay)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        using HttpRequestMessage asking = new(HttpMethod.Get, HybridcastRelayFeature.Relayed(started, relay, "http://hc.station.example.jp/p/a.js"));
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal("*", response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials));
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlExposeHeaders));
    }

    [Theory(DisplayName = "BR-BA-004: every answer of the frame's relays carries the CORS headers, whatever refused or failed it")]
    [InlineData("GET", "app", "outside the boundary", 403)]
    [InlineData("POST", "app", "outside the boundary", 403)]
    [InlineData("GET", "app", "a name that did not resolve", 502)]
    [InlineData("GET", "app", "a refused address", 502)]
    [InlineData("POST", "app", "a server out of reach", 502)]
    [InlineData("GET", "app", "no address", 400)]
    [InlineData("GET", "app", "a ticket that started nothing", 403)]
    [InlineData("GET", "app", "a ticket that is not one", 401)]
    [InlineData("GET", "app", "another channel", 403)]
    [InlineData("GET", "app", "a failure nothing answered for", 500)]
    [InlineData("POST", "app", "a failure nothing answered for", 500)]
    [InlineData("GET", "ws", "a page and not a WebSocket", 400)]
    [InlineData("GET", "ws", "a ticket that started nothing", 400)]
    public async Task EveryAnswerOfTheFramesRelaysCarriesTheCorsHeaders(string method, string relay, string what, int status)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;
        feature.Relay.Fetched = what switch
        {
            "a name that did not resolve" => _ => RelayedBody.Failed(RelayResult.NameNotResolved),
            "a refused address" => _ => RelayedBody.Failed(RelayResult.RefusedOrTooLarge),
            "a server out of reach" => _ => RelayedBody.Failed(RelayResult.Disconnected),
            "a failure nothing answered for" => _ => throw new InvalidOperationException("The relay failed."),
            _ => feature.Relay.Fetched,
        };
        string address = what == "outside the boundary" ? "http://elsewhere.example/a.js" : "http://hc.station.example.jp/p/a.js";
        string path = what switch
        {
            "no address" => $"/api/live/32736-1024/data-broadcast/{relay}/{ticket}",
            "a ticket that started nothing" => $"/api/live/32736-1024/data-broadcast/{relay}/{new string('A', 43)}?uri={Uri.EscapeDataString(address)}",
            "a ticket that is not one" => $"/api/live/32736-1024/data-broadcast/{relay}/{new string('~', 43)}?uri={Uri.EscapeDataString(address)}",
            "another channel" => $"/api/live/32736-1025/data-broadcast/{relay}/{ticket}?uri={Uri.EscapeDataString(address)}",
            _ => HybridcastRelayFeature.Relayed(started, relay, address).OriginalString,
        };
        using HttpRequestMessage asking = new(new HttpMethod(method), new Uri(path, UriKind.Relative));
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");

        if (method == "POST")
        {
            asking.Content = new StringContent("a=1", Encoding.UTF8, "application/x-www-form-urlencoded");
        }

        using HttpResponseMessage response = await feature.Frame.SendAsync(asking);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials));
    }

    [Theory(DisplayName = "BR-BA-004: no other route answers another origin, whether it carries a ticket in its path, is asked for plainly or is preflighted")]
    [InlineData("GET", "/api/health")]
    [InlineData("GET", "/api/live/32736-1024/with-ticket/{ticket}/live.ts")]
    [InlineData("GET", "/api/videos/sample/with-ticket/{ticket}/video.mp4")]
    [InlineData("OPTIONS", "/api/live/32736-1024/with-ticket/{ticket}/live.ts")]
    [InlineData("OPTIONS", "/api/live/32736-1024/data-broadcast/app/ticket")]
    [InlineData("OPTIONS", "/api/reservations")]
    public async Task NoOtherRouteAnswersAnotherOrigin(string method, string path)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        Uri target = new(path.Replace("{ticket}", started.GetProperty("inTheClear").GetString(), StringComparison.Ordinal), UriKind.Relative);
        using HttpRequestMessage asking = method == "OPTIONS" ? HybridcastRelayFeature.Preflight(target, "GET", string.Empty) : new HttpRequestMessage(HttpMethod.Get, target);
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");

        using HttpResponseMessage response = await feature.Viewer.SendAsync(asking);

        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowOrigin));
        Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowMethods));
    }

    [Theory]
    [InlineData("/api/recordings?standing=sideways", "sideways")]
    [InlineData("/api/recordings?outcome=nearly", "nearly")]
    [InlineData("/api/recordings?drops=maybe", "maybe")]
    [InlineData("/api/recordings?sort=alphabetically", "alphabetically")]
    [InlineData("/api/recordings?perPage=abc", "abc")]
    [InlineData("/api/recordings?from=whenever", "whenever")]
    [InlineData("/api/reservations?sort=sideways", "sideways")]
    [InlineData("/api/live/channels?fields=everything", "everything")]
    [InlineData("/api/quality/channels?sort=sideways", "sideways")]
    [InlineData("/api/encoding/jobs?status=nearly", "nearly")]
    public async Task AValueAnEndpointCannotReadIsRefusedInTheEnvelopeWithoutRepeatingWhatWasSent(
        string path,
        string sent)
    {
        await using var feature = new RecordingFeature();

        string body = await feature.GetTextAsync(path);
        (HttpStatusCode status, JsonElement answer) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(answer.GetProperty("status").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("message").GetString()));
        Assert.DoesNotContain(sent, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoSurfaceOfThisApplicationNamesWhereTheDriverSocketIs()
    {
        string wherever = SomewhereTheRuntimeWillTalkAbout();
        await using var host = new TestingWebApplicationFactory { DriverSocketPath = wherever };
        WebApplicationFactory<Program> guarded = host.WithTestScheme();
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

        await using var host = new TestingWebApplicationFactory { DriverSocketPath = absent };
        WebApplicationFactory<Program> guarded = host.WithTestScheme();
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

    [Fact]
    public async Task ASweepThatRanOutOfTimeSaysSoRatherThanReadingAsASurfaceNamingTheSocket()
    {
        await using var host = new TestingWebApplicationFactory();
        WebApplicationFactory<Program> guarded = host.WithTestScheme();
        using HttpClient client = Authenticated(guarded);
        client.Timeout = TimeSpan.FromTicks(1);

        SweepDidNotFinish gaveUp = await Assert.ThrowsAsync<SweepDidNotFinish>(
            () => EverySurfaceAsync(guarded, client));

        Assert.Contains("did not answer within", gaveUp.Message, StringComparison.Ordinal);
        Assert.Contains("not a surface naming the socket", gaveUp.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSurfacesTheSweepSkipsAreTheOnesWhoseAnswerNeverEnds()
    {
        await using var host = new TestingWebApplicationFactory();

        string[] routed = [.. RouteInventory.Of(host).Select(surface => surface.Pattern)];

        Assert.Equal(
            ["/api/events", "/api/programs/bulk"],
            NotAskedBecauseTheAnswerNeverEnds.Order(StringComparer.Ordinal).ToArray());
        Assert.All(
            NotAskedBecauseTheAnswerNeverEnds,
            skipped => Assert.Contains(skipped, routed, StringComparer.Ordinal));
    }

    private static bool UnderTheMigrationRoot(RoutedSurface surface)
        => string.Equals(surface.Pattern, MigrationRoot, StringComparison.Ordinal)
           || surface.Pattern.StartsWith(MigrationRoot + "/", StringComparison.Ordinal);

    private IEnumerable<RoutedSurface> MigrationSurfaces() => Inventory().Where(UnderTheMigrationRoot);

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);

    private static bool UnderARoot(RoutedSurface surface)
        => StreamingRoots.Any(root => string.Equals(surface.Pattern, root, StringComparison.Ordinal)
                                      || surface.Pattern.StartsWith(root + "/", StringComparison.Ordinal));

    private IEnumerable<RoutedSurface> Streaming() => Inventory().Where(UnderARoot);

    private static HttpClient Carrying(AuthProbe probe, string? ticket)
    {
        HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        if (ticket is not null)
        {
            player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        }

        return player;
    }

    private static async Task<string> IssuedAsync(AuthProbe probe)
    {
        await probe.SignedInAsync();

        using HttpResponseMessage answer = await probe.Client.PostAsJsonAsync(
            new Uri("/api/live/ticket", UriKind.Relative),
            new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;
    }

    private AuthProbe Wiring(out Recording recording)
    {
        HeldServices services = new();
        HeldCandidates candidates = new();
        HeldRecordings recordings = new();
        NetworkId network = new(32736);
        ServiceId service = new(1024);
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            network,
            service,
            TuningParameters.Terrestrial(27),
            At);

        candidate.Select(SelectionSource.Manual, null, At);
        services.Services.Add(BroadcastService.Discover(network, service, "Watched", ServiceCategory.Television, At));
        candidates.Candidates.Add(candidate);

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(wired =>
        {
            wired.AddSingleton<IBroadcastServiceRepository>(services);
            wired.AddSingleton<ICandidateChannelRepository>(candidates);
            wired.AddSingleton<IRecordingDirectory>(recordings);
            wired.AddSingleton<ILiveSupply>(supply);
        });
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

    private static string SomewhereTheRuntimeWillTalkAbout()
        => Path.Combine(
            Directory.CreateTempSubdirectory("carina-socket-of-this-host-").FullName,
            new string('x', 200) + ".sock");

    private static HttpClient Authenticated(WebApplicationFactory<Program> guarded)
    {
        HttpClient client = guarded.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        client.Timeout = TimeSpan.FromSeconds(20);

        return client;
    }

    private static async Task<IReadOnlyList<AnsweredSurface>> EverySurfaceAsync(
        WebApplicationFactory<Program> guarded,
        HttpClient client)
    {
        var answered = new List<AnsweredSurface>();

        foreach (RoutedSurface surface in RouteInventory.Of(guarded))
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

            answered.Add(await AnsweredAsync(client, surface, asking));
        }

        return answered;
    }

    private static async Task<AnsweredSurface> AnsweredAsync(
        HttpClient client,
        RoutedSurface surface,
        HttpRequestMessage asking)
    {
        try
        {
            using HttpResponseMessage response = await client.SendAsync(asking);

            return new AnsweredSurface(
                surface.ToString(),
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync());
        }
        catch (Exception cut) when (cut is OperationCanceledException or HttpRequestException)
        {
            throw new SweepDidNotFinish(
                $"{surface} did not answer within {client.Timeout}, so the sweep never reached the rule. "
                + "This is the sweep giving up, not a surface naming the socket: read it as a machine too "
                + "slow to answer every surface in time, and look at what else on the run was slow.",
                cut);
        }
    }

    private sealed class SweepDidNotFinish(string said, Exception cut) : Exception(said, cut);
}
