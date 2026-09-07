using System.Text.Json.Nodes;

using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DeletionTakesOneRecordingWholeTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] WordsForManyAtOnce =
        ["bulk", "batch", "selected", "selection", "many"];

    private static readonly string[] WordsForKeepingOneBack =
        ["protect", "protected", "locked", "pinned", "favourite", "favorite"];

    private static readonly string[] WordsForBringingOneBack =
        ["restore", "undelete", "trash", "recycle", "deletedat"];

    private static readonly string[] WhereARecordingIsReached = ["/api/recordings", "/api/library"];

    [Fact]
    public void TheRecordingSurfaceIsTheOneThisSweepWatches()
    {
        Assert.NotEmpty(AboutARecording());
    }

    [Fact]
    public void EverySurfaceThatDeletesNamesTheOneThingItDeletes()
    {
        Assert.Empty(Deleting()
            .Where(surface => !surface.Pattern.EndsWith('}'))
            .Select(surface => surface.ToString())
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NoSurfaceOffersToThrowAwayMoreThanOneRecordingAtATime()
    {
        Assert.Empty(SurfacesNaming(WordsForManyAtOnce));
    }

    [Fact]
    public void NoSurfaceOffersToKeepARecordingBackFromBeingThrownAway()
    {
        Assert.Empty(SurfacesNaming(WordsForKeepingOneBack));
    }

    [Fact]
    public void NoSurfaceOffersToBringAThrownAwayRecordingBack()
    {
        Assert.Empty(SurfacesNaming(WordsForBringingOneBack));
    }

    [Fact]
    public void TheOnlyWayToThrowARecordingAwayTakesTheWholeOfIt()
    {
        Assert.Equal(
            ["DELETE /api/recordings/{id}"],
            EndpointRules.SurfacesThatDeleteUnder(Inventory(), "/api/recordings"));
    }

    [Fact]
    public async Task NothingSentToADeletionSaysWhichPartOfTheRecordingToThrowAway()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        Assert.Empty(Deletions(document)
            .Where(operation => operation.Value["requestBody"] is not null
                || Parameters(operation.Value).Any(parameter => !IsOnThePath(parameter)))
            .Select(operation => $"{operation.Method} {operation.Path}")
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NothingTheDocumentDescribesCarriesAMarkThatKeepsARecordingBack()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        Assert.Empty(document["components"]!["schemas"]!
            .AsObject()
            .Where(schema => schema.Key.Contains("Recording", StringComparison.Ordinal))
            .SelectMany(schema => Properties(schema.Value!).Select(named => (schema.Key, Property: named)))
            .Where(named => Names(named.Property, WordsForKeepingOneBack)
                || Names(named.Property, WordsForBringingOneBack))
            .Select(named => $"{named.Key}.{named.Property}")
            .Order(StringComparer.Ordinal));
    }

    private static IEnumerable<(string Path, string Method, JsonObject Value)> Deletions(JsonNode document)
        => ServedOpenApi.Operations(document)
            .Where(operation => string.Equals(operation.Method, "delete", StringComparison.Ordinal));

    private static IEnumerable<JsonNode> Parameters(JsonObject operation)
        => operation["parameters"]?.AsArray().OfType<JsonNode>() ?? [];

    private static bool IsOnThePath(JsonNode parameter)
        => string.Equals(parameter["in"]?.GetValue<string>(), "path", StringComparison.Ordinal);

    private static IEnumerable<string> Properties(JsonNode schema)
        => schema["properties"]?.AsObject().Select(property => property.Key) ?? [];

    private static bool Names(string text, IReadOnlyList<string> words)
    {
        string folded = text.ToLowerInvariant();

        return folded.Split('-').Any(words.Contains) || words.Contains(folded);
    }

    private static IEnumerable<string> Segments(string pattern)
        => pattern.Split('/').Where(segment => !segment.StartsWith('{'));

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);

    private IEnumerable<RoutedSurface> Deleting()
        => Inventory().Where(surface => HttpMethods.IsDelete(surface.Method));

    private IEnumerable<RoutedSurface> AboutARecording()
        => Inventory().Where(surface => WhereARecordingIsReached.Any(
            root => surface.Pattern.StartsWith(root, StringComparison.Ordinal)));

    private IEnumerable<string> SurfacesNaming(IReadOnlyList<string> words)
        => AboutARecording()
            .Where(surface => Segments(surface.Pattern).Any(segment => Names(segment, words)))
            .Select(surface => surface.ToString())
            .Order(StringComparer.Ordinal);
}
