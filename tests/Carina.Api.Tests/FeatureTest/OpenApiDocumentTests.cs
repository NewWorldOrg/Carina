using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class OpenApiDocumentTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task EveryDescribedResponseIsJsonOnly()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var declared = ServedOpenApi.Operations(document)
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
        var document = await ServedOpenApi.FetchAsync(factory);

        var withoutRefusal = ServedOpenApi.Operations(document)
            .Where(operation => operation.Path != "/api/health")
            .Where(operation => operation.Value["responses"]!["401"] is null)
            .Select(operation => $"{operation.Method} {operation.Path}")
            .ToArray();

        Assert.Empty(withoutRefusal);
    }

    [Fact]
    public async Task TheAnonymousEndpointDoesNotClaimItCanRefuse()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var health = document["paths"]!["/api/health"]!["get"]!["responses"]!.AsObject();

        Assert.Null(health["401"]);
    }

    [Fact]
    public async Task TheRefusalCarriesNoBodyBecauseTheMiddlewareSendsNone()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        using var client = factory.WithTestScheme().CreateClient();
        using var response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(
            document["paths"]!["/api/driver/status"]!["get"]!["responses"]!["401"]!["content"]
        );
    }

    [Fact]
    public async Task TheConnectionEnumIsSpelledTheWayTheEndpointSpellsIt()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var connection = body.RootElement.GetProperty("data").GetProperty("connection").GetString();

        var document = await ServedOpenApi.FetchAsync(factory);
        var spellings = document["components"]!["schemas"]!["DriverConnection"]!["enum"]!
            .AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();

        Assert.Contains(connection, spellings);
    }

    [Fact]
    public async Task EveryOperationCarriesTheNameItsClientWillBeGeneratedFrom()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var named = ServedOpenApi.Operations(document)
            .Select(operation => operation.Value["operationId"]?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "addCandidateChannel",
                "applyScan",
                "cancelScan",
                "deleteCandidateChannel",
                "getDetectedTuners",
                "getDriverStatus",
                "getHealth",
                "getScan",
                "getService",
                "getTuners",
                "listScanRuns",
                "listServices",
                "patchTuner",
                "putSelectedChannel",
                "putTuners",
                "restartDriver",
                "startScan",
            ],
            named.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(named.Length, named.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task NoNameInTheDocumentIsAnInternalClassName()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var tags = ServedOpenApi.Operations(document)
            .SelectMany(operation => operation.Value["tags"]!.AsArray())
            .Select(tag => tag!.GetValue<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = document["tags"]!
            .AsArray()
            .Select(tag => tag!["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(["tuners", "services", "health", "driver"], tags);
        Assert.Equal(tags, declared);
        Assert.DoesNotContain(tags, tag => tag.EndsWith("Action", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDocumentPointsAtTheSameOriginRatherThanAHost()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var servers = document["servers"]!
            .AsArray()
            .Select(server => server!["url"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(["/"], servers);
    }

    [Fact]
    public async Task EveryEnumSaysWhatItsValuesAre()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var untyped = document["components"]!["schemas"]!
            .AsObject()
            .Where(schema => schema.Value!["enum"] is not null)
            .Where(schema => schema.Value!["type"]?.GetValue<string>() != "string")
            .Select(schema => schema.Key)
            .ToArray();

        Assert.Empty(untyped);
    }

    [Fact]
    public async Task TheEnvelopeIsDescribedRatherThanLeftOpaque()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        var envelope = document["components"]!["schemas"]!["BaseResponderOfDriverStatusResponder"]!;
        var properties = envelope["properties"]!.AsObject().Select(entry => entry.Key).ToArray();

        Assert.Equal(["status", "message", "data"], properties);
        Assert.Equal("boolean", envelope["properties"]!["status"]!["type"]!.GetValue<string>());
        Assert.Equal("string", envelope["properties"]!["message"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task BothEndpointsAnswerInTheMediaTypeTheDocumentPromises()
    {
        using var anonymous = factory.CreateClient();
        using var authenticated = factory.CreateAuthenticatedClient();

        using var health = await anonymous.GetAsync(new Uri("/api/health", UriKind.Relative));
        using var status = await authenticated.GetAsync(
            new Uri("/api/driver/status", UriKind.Relative)
        );

        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", status.Content.Headers.ContentType?.MediaType);
    }
}
