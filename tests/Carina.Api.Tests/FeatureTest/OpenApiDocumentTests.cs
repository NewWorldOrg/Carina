using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class OpenApiDocumentTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task EveryDescribedResponseIsJsonOnly()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] declared = ServedOpenApi.Operations(document)
            .SelectMany(operation => operation.Value["responses"]!.AsObject())
            .Select(response => response.Value!["content"]?.AsObject())
            .Where(content => content is not null)
            .SelectMany(content => content!.Select(entry => entry.Key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["application/json"], declared);
    }

    [Fact]
    public async Task EveryOperationBehindTheDefaultDenyDeclaresItsRefusal()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] withoutRefusal = ServedOpenApi.Operations(document)
            .Where(operation => !AnonymousSurfaces.WhileDeveloping.Admit(operation.Method, operation.Path))
            .Where(operation => operation.Value["responses"]!["401"] is null)
            .Select(operation => $"{operation.Method} {operation.Path}")
            .ToArray();

        Assert.Empty(withoutRefusal);
    }

    [Fact]
    public async Task TheAnonymousEndpointsDoNotClaimTheyCanRefuse()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        JsonObject health = document["paths"]!["/api/health"]!["get"]!["responses"]!.AsObject();
        JsonObject options = document["paths"]![SignInOptions.Path]!["get"]!["responses"]!.AsObject();

        Assert.Null(health["401"]);
        Assert.Null(options["401"]);
    }

    [Fact]
    public async Task TheRefusalCarriesNoBodyBecauseTheMiddlewareSendsNone()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        using HttpClient client = factory.WithTestScheme().CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(
            document["paths"]!["/api/driver/status"]!["get"]!["responses"]!["401"]!["content"]
        );
    }

    [Fact]
    public async Task TheConnectionEnumIsSpelledTheWayTheEndpointSpellsIt()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? connection = body.RootElement.GetProperty("data").GetProperty("connection").GetString();

        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        string[] spellings = document["components"]!["schemas"]!["DriverConnection"]!["enum"]!
            .AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();

        Assert.Contains(connection, spellings);
    }

    [Fact]
    public async Task EveryOperationCarriesTheNameItsClientWillBeGeneratedFrom()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] named = ServedOpenApi.Operations(document)
            .Select(operation => operation.Value["operationId"]?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "addCandidateChannel",
                "applyRuleNow",
                "applyScan",
                "cancelReservation",
                "cancelScan",
                "changePassword",
                "collectNow",
                "createReservation",
                "createRule",
                "deleteCandidateChannel",
                "deleteRecording",
            "deleteReservation",
                "deleteRule",
                "deleteSession",
                "forgetArchivedService",
                "getCollectionStatus",
                "getDetectedTuners",
                "getDriverStatus",
                "getHealth",
                "getMe",
                "getOidcConfig",
                "getProgramme",
                "getProgrammeGuide",
                "getRecording",
                "getRecordingIntegrity",
                "getReservation",
                "getRule",
                "getScan",
                "getService",
                "getSessions",
                "getSignInOptions",
                "getStorage",
                "getTunerHealth",
                "getTuners",
                "impactOfRules",
                "listRecordings",
                "listReservations",
                "listRules",
                "listScanRuns",
                "listServices",
                "logIn",
                "logOut",
                "patchTuner",
                "previewRules",
                "putOidcConfig",
                "putSelectedChannel",
                "putTunerHealthSettings",
                "putTuners",
                "rebuildEpg",
                "remakeThumbnail",
                "replaceRule",
                "restartDriver",
                "restoreReservation",
                "reviseReservation",
                "runRecordingIntegrityCheck",
                "searchProgrammes",
                "startScan",
                "stopRecording",
                "switchRule",
            ],
            named.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(named.Length, named.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task NoNameInTheDocumentIsAnInternalClassName()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] tags = ServedOpenApi.Operations(document)
            .SelectMany(operation => operation.Value["tags"]!.AsArray())
            .Select(tag => tag!.GetValue<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string[] declared = document["tags"]!
            .AsArray()
            .Select(tag => tag!["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(
            [
                "tuners",
                "storage",
                "services",
                "rules",
                "reservations",
                "recordings",
                "health",
                "epg",
                "programs",
                "driver",
                "auth",
            ],
            tags);
        Assert.Equal(tags, declared);
        Assert.DoesNotContain(tags, tag => tag.EndsWith("Action", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDocumentPointsAtTheSameOriginRatherThanAHost()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] servers = document["servers"]!
            .AsArray()
            .Select(server => server!["url"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(["/"], servers);
    }

    [Fact]
    public async Task EveryEnumSaysWhatItsValuesAre()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] untyped = document["components"]!["schemas"]!
            .AsObject()
            .Where(schema => schema.Value!["enum"] is not null)
            .Where(schema => !SaysItIsAString(schema.Value!["type"]))
            .Select(schema => schema.Key)
            .ToArray();

        Assert.Empty(untyped);
    }

    [Fact]
    public async Task AnEnumAValueCanBeAbsentFromSaysSoBesideTheValuesItHas()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode outcome = document["components"]!["schemas"]!["RecordingOutcome"]!;
        string[] values = [.. outcome["enum"]!.AsArray().Select(value => value?.GetValue<string>() ?? "absent")];

        Assert.Equal(
            ["null", "string"],
            outcome["type"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal(["complete", "truncated", "failed", "absent"], values);
    }

    [Fact]
    public async Task AStateChangingPostWithNoBodyDescribesNoBodyThoughOneStillHasToNameJson()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode run = document["paths"]!["/api/recordings/integrity/run"]!["post"]!;
        JsonNode picture = document["paths"]!["/api/recordings/{id}/thumbnail"]!["post"]!;

        Assert.Null(run["requestBody"]);
        Assert.Null(picture["requestBody"]);
    }

    [Fact]
    public async Task AShortfallAValueCanBeAbsentFromSaysSoBesideTheValuesItHas()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode shortfall = document["components"]!["schemas"]!["DiskShortfall"]!;

        Assert.Equal(
            ["null", "string"],
            shortfall["type"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal(
            [
                "rootsUnknown",
                "rootUndeclared",
                "rootUnmeasured",
                "rootNotWritable",
                "noRoomLeft",
                "shortOfTheEstimate",
                "absent",
            ],
            shortfall["enum"]!.AsArray().Select(value => value?.GetValue<string>() ?? "absent").ToArray());
    }

    [Fact]
    public async Task TheClassesASweepCanNameAreSpelledInTheDocumentTheWayTheEndpointSpellsThem()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode fault = document["components"]!["schemas"]!["IntegrityFault"]!;

        Assert.Equal("string", fault["type"]!.GetValue<string>());
        Assert.Equal(
            ["sizeDisagrees", "noLedgerRow", "fileMissing", "fileEmpty", "emptyThoughComplete"],
            fault["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task TheWaysASweepCanBeRefusedAreSpelledInTheDocument()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode refusal = document["components"]!["schemas"]!["SweepRefusal"]!;

        Assert.Equal("string", refusal["type"]!.GetValue<string>());
        Assert.Equal(
            ["none", "oneIsAlreadyRunning", "tooSoonAfterTheLastOne"],
            refusal["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task WhatARootAnswersIsDescribedFieldByFieldAndNoneOfThemIsAPath()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode root = document["components"]!["schemas"]!["StorageRootResponder"]!;

        Assert.Equal(
            ["name", "freeBytes", "totalBytes", "writable", "committedBytes", "recordingsInFlight", "shortfall"],
            root["properties"]!.AsObject().Select(entry => entry.Key).ToArray());
    }

    private static bool SaysItIsAString(JsonNode? declared)
        => declared switch
        {
            JsonArray named => named.Any(value => value?.GetValue<string>() == "string"),
            JsonValue named => named.GetValue<string>() == "string",
            _ => false,
        };

    [Fact]
    public async Task TheEnvelopeIsDescribedRatherThanLeftOpaque()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        JsonNode envelope = document["components"]!["schemas"]!["BaseResponderOfDriverStatusResponder"]!;
        string[] properties = envelope["properties"]!.AsObject().Select(entry => entry.Key).ToArray();

        Assert.Equal(["status", "message", "data"], properties);
        Assert.Equal("boolean", envelope["properties"]!["status"]!["type"]!.GetValue<string>());
        Assert.Equal("string", envelope["properties"]!["message"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task BothEndpointsAnswerInTheMediaTypeTheDocumentPromises()
    {
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient authenticated = factory.CreateAuthenticatedClient();

        using HttpResponseMessage health = await anonymous.GetAsync(new Uri("/api/health", UriKind.Relative));
        using HttpResponseMessage status = await authenticated.GetAsync(
            new Uri("/api/driver/status", UriKind.Relative)
        );

        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", status.Content.Headers.ContentType?.MediaType);
    }
}
