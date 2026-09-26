using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Common;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class OpenApiDocumentTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task TheDocumentIsStampedWithTheVersionTheApplicationWasBuiltAs()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        Assert.Equal(
            DeclaredVersion.Of(typeof(DeclaredVersion).Assembly),
            document["info"]!["version"]!.GetValue<string>());
    }

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
                "cancelEncodeJob",
                "cancelReservation",
                "cancelScan",
                "changePassword",
                "collectNow",
                "createEncodeDestination",
                "createEncodeProfile",
                "createReservation",
                "createRule",
                "deleteCandidateChannel",
                "deleteIntegrityFinding",
                "deleteRecording",
            "deleteReservation",
                "deleteRule",
                "deleteSession",
                "forgetArchivedService",
                "getCollectionStatus",
                "getDetectedTuners",
                "getDriverStatus",
                "getEncodeDurations",
                "getEncodeSettings",
                "getHealth",
                "getMe",
                "getMigrationRecord",
                "getOidcConfig",
                "getProgramme",
                "getProgrammeGuide",
                "getQualitySummary",
                "getQualitySupplyHealth",
                "getQualityTrends",
                "getRecording",
                "getRecordingIntegrity",
                "getReservation",
                "getReservationHealth",
                "getRule",
                "getScan",
                "getService",
                "getServiceLogo",
                "getSessions",
                "getSignInOptions",
                "getStorage",
                "getTunerHealth",
                "getTuners",
                "getVersion",
                "getVideoScrubFrame",
                "getVideoThumbnail",
                "impactOfRules",
                "issueLiveTicket",
                "issueVideoTicket",
                "listEncodeDestinations",
                "listEncodeJobs",
                "listEncodeProfiles",
                "listLiveChannels",
                "listLiveDepartures",
                "listLiveProfiles",
                "listLiveSessions",
                "listQualityCandidateScores",
                "listQualityChannels",
                "listQualityIncidents",
                "listQualityRecordings",
                "listQualityThresholds",
                "listQualityTuners",
                "listRecordings",
                "listReservationOutcomes",
                "listReservations",
                "listRules",
                "listScanRuns",
                "listServices",
                "logIn",
                "logOut",
                "patchTuner",
                "playVideo",
                "previewRules",
                "putEncodeSettings",
                "putOidcConfig",
                "putPlaybackPosition",
                "putSelectedChannel",
                "putTunerHealthSettings",
                "putTuners",
                "queueEncodeJob",
                "rebuildEpg",
                "remakeThumbnail",
                "removeEncodeDestination",
                "removeEncodeProfile",
                "replaceRule",
                "restartDriver",
                "restoreReservation",
                "reviseEncodeDestination",
                "reviseEncodeProfile",
                "reviseQualityThreshold",
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
                "videos",
                "services",
                "version",
                "tuners",
                "storage",
                "rules",
                "reservations",
                "recordings",
                "quality",
                "migration",
                "live",
                "health",
                "epg",
                "programs",
                "encoding",
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
    public async Task TheWaysARecordingRequestCanBeRefusedAreSpelledInTheDocument()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode refusal = document["components"]!["schemas"]!["RecordingFailure"]!;

        Assert.Equal(
            [
                "noSuchRecording",
                "alreadyEnded",
                "notBeingWritten",
                "stillRecording",
                "driverUnreachable",
                "driverRefused",
                "nowhereToPutPictures",
                "fileOutOfReach",
                "rootOutOfReach",
                "filesLeftBehind",
                "oneIsAlreadyBeingDiscarded",
                "tookTooLong",
                "beingEncoded",
            ],
            refusal["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
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

    [Fact]
    public async Task WhatARecordingAnswersIsDescribedFieldByFieldWithWhenItWasWeighedAndTheEndItWasPromised()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode properties = document["components"]!["schemas"]!["RecordingResponder"]!["properties"]!;

        Assert.Equal(
            [
                "id", "reservationId", "programme", "standing", "outcome", "outcomeDetail", "startedAt", "stoppedAt",
                "abortedAt", "expectedWindow", "promisedWindowEnd", "writtenDurationMs", "resumeCount", "fileSizeBytes",
                "observedAt", "outputRoot", "fileName", "tunerDeviceId", "drops", "thumbnail", "broadcastGroup", "encode",
                "unfinishedDeletion", "leftScrambled", "descrambledAt",
            ],
            properties.AsObject().Select(entry => entry.Key).ToArray());

        foreach (string time in new[] { "promisedWindowEnd", "observedAt" })
        {
            Assert.True(SaysItIsAString(properties[time]!["type"]), time);
            Assert.Equal("date-time", properties[time]!["format"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task WhatARecordingCountedIsDescribedWithItsScramblingJudgedApartInTheSameVocabulary()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode properties = document["components"]!["schemas"]!["RecordingDropsResponder"]!["properties"]!;

        Assert.Equal(
            [
                "quality", "scrambleQuality", "ccMeasured", "ccDroppedPackets", "ccTotalPackets", "scrambledPackets",
                "eovfCount", "measuredUpdatedAt",
            ],
            properties.AsObject().Select(entry => entry.Key).ToArray());
        Assert.Equal(properties["quality"]!.ToJsonString(), properties["scrambleQuality"]!.ToJsonString());
    }

    [Fact]
    public async Task WhatARecordingSaysAboutEncodingCarriesWhetherItAsksForOneBesideWhereItStands()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode encode = document["components"]!["schemas"]!["RecordingEncodeResponder"]!;
        JsonNode properties = encode["properties"]!;

        Assert.Equal(
            ["standing", "whenRecorded"],
            properties.AsObject().Select(entry => entry.Key).ToArray());
        Assert.Equal("boolean", properties["whenRecorded"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["standing", "whenRecorded"],
            encode["required"]!.AsArray().Select(name => name!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("RecordingResponder")]
    [InlineData("ReservationOutcomeResponder")]
    public async Task WhatWasLeftScrambledIsAlwaysAnsweredBesideWhenItWasDescrambled(string schema)
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode described = document["components"]!["schemas"]!.AsObject()[schema]!;
        JsonNode properties = described["properties"]!;
        string[] required = [.. described["required"]!.AsArray().Select(name => name!.GetValue<string>())];

        Assert.Equal("boolean", properties["leftScrambled"]!["type"]!.GetValue<string>());
        Assert.True(SaysItIsAString(properties["descrambledAt"]!["type"]));
        Assert.Equal("date-time", properties["descrambledAt"]!["format"]!.GetValue<string>());
        Assert.Contains("null", properties["descrambledAt"]!["type"]!.AsArray().Select(type => type!.GetValue<string>()));
        Assert.Contains("leftScrambled", required);
        Assert.Contains("descrambledAt", required);
    }

    private static bool SaysItIsAString(JsonNode? declared)
        => declared switch
        {
            JsonArray named => named.Any(value => value?.GetValue<string>() == "string"),
            JsonValue named => named.GetValue<string>() == "string",
            _ => false,
        };

    [Fact]
    public async Task ThePlanOfARecordingDescribesTheChaptersItCarriesAsAShapeOfItsOwn()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();
        JsonNode chapters = schemas["PlaybackPlanResponder"]!["properties"]!["chapters"]!;

        Assert.Equal("array", chapters["type"]!.GetValue<string>());
        Assert.EndsWith(
            "/PlaybackChapterResponder",
            chapters["items"]!["$ref"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.Equal(
            ["startsAtSec", "endsAtSec", "kind"],
            schemas["PlaybackChapterResponder"]!["properties"]!.AsObject().Select(entry => entry.Key).ToArray());
    }

    [Fact(DisplayName = "A-配信-074: the plan says which of the two files it plays and which other one it could be asked for")]
    public async Task ThePlanNamesWhatItPlaysAndTheOtherOneItCouldBeAskedFor()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode plan = document["components"]!["schemas"]!["PlaybackPlanResponder"]!;
        string[] properties = [.. plan["properties"]!.AsObject().Select(entry => entry.Key)];
        string[] required = [.. plan["required"]!.AsArray().Select(name => name!.GetValue<string>())];

        Assert.Contains("source", properties, StringComparer.Ordinal);
        Assert.Contains("alternative", properties, StringComparer.Ordinal);
        Assert.Contains("source", required, StringComparer.Ordinal);
        Assert.Contains("alternative", required, StringComparer.Ordinal);
        Assert.EndsWith(
            "/PlaybackSource",
            plan["properties"]!["source"]!["$ref"]!.GetValue<string>(),
            StringComparison.Ordinal);

        string[] otherOne =
        [
            .. plan["properties"]!["alternative"]!["oneOf"]!
                .AsArray()
                .Select(said => said!["type"]?.GetValue<string>() ?? said!["$ref"]!.GetValue<string>()),
        ];

        Assert.Contains("null", otherOne, StringComparer.Ordinal);
        Assert.Contains(otherOne, said => said.EndsWith("/PlaybackSource", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "A-配信-074: the two things a recording can be played from are spelled in the document the way the plan spells them")]
    public async Task TheTwoThingsARecordingCanBePlayedFromAreSpelledInTheDocumentTheWayThePlanSpellsThem()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode source = document["components"]!["schemas"]!["PlaybackSource"]!;

        Assert.Equal(
            ["artefact", "recording"],
            source["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task TheKindsAChapterCanBeAreSpelledInTheDocumentTheWayThePlanSpellsThem()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode kind = document["components"]!["schemas"]!["ChapterKind"]!;

        Assert.Equal("string", kind["type"]!.GetValue<string>());
        Assert.Equal(
            ["programme", "break"],
            kind["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    [Fact(DisplayName = "A-エンコード-069: what queues a job names the recording, the profile and the destination, and says whether the artefact is to be made again")]
    public async Task WhatQueuesAJobSaysWhetherTheArtefactIsToBeMadeAgain()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode properties = document["components"]!["schemas"]!["QueueEncodeJobRequest"]!["properties"]!;

        Assert.Equal(
            ["recordingId", "profileId", "destinationId", "makeItAgain"],
            properties.AsObject().Select(entry => entry.Key).ToArray());
        Assert.Contains("boolean", properties["makeItAgain"]!.ToJsonString(), StringComparison.Ordinal);
    }

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
