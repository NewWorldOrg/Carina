using System.Text.Json.Nodes;

using Carina.Api.Playback;

namespace Carina.Api.Tests.FeatureTest;

public sealed class DocumentedInputTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] TheOnesTheDocumentDisowns =
    [
        "/api/live/{networkId:int}-{serviceId:int}/stream ticket",
        "/api/programs/bulk cursor",
        "/api/programs/bulk rows",
        "/api/videos/{id} source",
        "/api/videos/{id} ticket",
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
        Assert.Null(from["schema"]!["default"]);
        Assert.Contains(
            "where this reader last left this recording",
            from["description"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.Equal("string", profile["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["1080p60", "1080p30", "720p60", "720p30"],
            profile["schema"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Null(profile["schema"]!["default"]);
        Assert.Contains(
            "/api/live/profiles",
            profile["description"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlayingSaysWhichSoundsItCarriesAndThatNamingNoneCarriesTheMainOne()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode sound = Parameter(document, PlayDelivery.Path, PlayDelivery.Sound);

        Assert.Equal("query", sound["in"]!.GetValue<string>());
        Assert.Equal("string", sound["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["main", "secondary", "third"],
            sound["schema"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal("main", sound["schema"]!["default"]!.GetValue<string>());
    }

    [Fact(DisplayName = "the playing says which of the two files it can be played from and that naming none plays the artefact")]
    public async Task ThePlayingSaysWhichOfTheTwoFilesItPlaysAndThatNamingNonePlaysTheArtefact()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode source = Parameter(document, PlayDelivery.Path, PlayDelivery.Source);

        Assert.Equal("query", source["in"]!.GetValue<string>());
        Assert.Equal("string", source["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["artefact", "recording"],
            source["schema"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal("artefact", source["schema"]!["default"]!.GetValue<string>());
        Assert.Contains(
            "transcodes the recording itself while playing even where an artefact was made of it",
            source["description"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Theory(DisplayName = "the playing and its captions say which picture codings a browser can name, each as a repeat of the name")]
    [InlineData(PlayDelivery.Path, PlayDelivery.Decodes)]
    [InlineData(CaptionDelivery.Path, CaptionDelivery.Decodes)]
    public async Task ThePlayingAndItsCaptionsSayWhichPictureCodingsABrowserCanName(string surface, string name)
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode decodes = Parameter(document, surface, name);

        Assert.Equal("query", decodes["in"]!.GetValue<string>());
        Assert.Equal("form", decodes["style"]?.GetValue<string>() ?? "form");
        Assert.True(decodes["explode"]?.GetValue<bool>() ?? true);
        Assert.Equal("array", decodes["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            ["h264", "h265"],
            decodes["schema"]!["items"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Contains("hvc1", decodes["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the scan that keeps every query in the document sees the one that chooses which of the two files is played")]
    public void TheScanSeesTheQueryThatChoosesWhichOfTheTwoFilesIsPlayed()
    {
        Assert.Contains(
            $"{PlayDelivery.Path} {PlayDelivery.Source}",
            QueryInputScan.WhatEachSurfaceReads(QueryInputScan.ApiDirectory).Select(read => read.ToString()),
            StringComparer.Ordinal);
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
