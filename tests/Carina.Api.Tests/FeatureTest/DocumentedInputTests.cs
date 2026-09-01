using System.Text.Json.Nodes;

using Carina.Api.Playback;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DocumentedInputTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] TheOnesTheDocumentDisowns =
    [
        "/api/programs/bulk cursor",
        "/api/programs/bulk rows",
    ];

    [Fact]
    public async Task EveryQueryNameASurfaceReadsIsInTheDocumentBesideIt()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] absent =
        [
            .. QueryInputScan.WhatEachSurfaceReads(QueryInputScan.ApiDirectory)
                .Where(read => document["paths"]![read.Surface] is not null)
                .Where(read => !Documented(document, read.Surface).Contains(read.Name, StringComparer.Ordinal))
                .Select(read => read.ToString()),
        ];

        Assert.Empty(absent);
    }

    [Fact]
    public async Task TheSurfacesReadingAQueryOutsideTheDocumentAreTheOnesTheDocumentDisowns()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        Assert.Equal(
            TheOnesTheDocumentDisowns,
            QueryInputScan.WhatEachSurfaceReads(QueryInputScan.ApiDirectory)
                .Where(read => document["paths"]![read.Surface] is null)
                .Select(read => read.ToString())
                .ToArray());
    }

    [Fact]
    public void EveryQueryNameTheScanFindsIsOneItCanPlace()
    {
        Assert.Empty(QueryInputScan.WhatTheScanCouldNotPlace(QueryInputScan.ApiDirectory));
    }

    [Fact]
    public async Task TheFrameSaysWhichSecondItIsTakenFromAndThatSecondsAreWhatItCounts()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode position = Parameter(document, ScrubDelivery.Path, ScrubDelivery.Position);

        Assert.Equal("query", position["in"]!.GetValue<string>());
        Assert.Equal("number", position["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(0, position["schema"]!["default"]!.GetValue<double>());
        Assert.Contains("second", position["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlayingSaysWhereItStartsAndWhichProfileItIsEncodedIn()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode from = Parameter(document, PlayDelivery.Path, PlayDelivery.Position);
        JsonNode profile = Parameter(document, PlayDelivery.Path, PlayDelivery.Quality);

        Assert.Equal("number", from["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(0, from["schema"]!["default"]!.GetValue<double>());
        Assert.Equal("string", profile["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["1080p60", "1080p30", "720p60", "720p30"],
            profile["schema"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal(PlayDelivery.Ordinarily.Name, profile["schema"]!["default"]!.GetValue<string>());
    }

    [Fact]
    public async Task NoQueryInputIsAskedForAsSomethingTheCallerHasToSend()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string[] demanded =
        [
            .. QueryInputScan.WhatEachSurfaceReads(QueryInputScan.ApiDirectory)
                .Where(read => document["paths"]![read.Surface] is not null)
                .Where(read => Required(document, read.Surface, read.Name))
                .Select(read => read.ToString()),
        ];

        Assert.Empty(demanded);
    }

    private static IEnumerable<string> Documented(JsonNode document, string surface)
        => Parameters(document, surface)
            .Where(parameter => parameter!["in"]!.GetValue<string>() == "query")
            .Select(parameter => parameter!["name"]!.GetValue<string>());

    private static bool Required(JsonNode document, string surface, string name)
        => Parameters(document, surface)
            .Where(parameter => parameter!["name"]!.GetValue<string>() == name)
            .Any(parameter => parameter!["required"]?.GetValue<bool>() is true);

    private static IEnumerable<JsonNode?> Parameters(JsonNode document, string surface)
        => document["paths"]![surface]!
            .AsObject()
            .SelectMany(operation => operation.Value!["parameters"]?.AsArray() ?? []);

    private static JsonNode Parameter(JsonNode document, string surface, string name)
    {
        JsonNode? found = Parameters(document, surface)
            .FirstOrDefault(parameter => parameter!["name"]!.GetValue<string>() == name);

        Assert.NotNull(found);

        return found;
    }
}
