using System.Text.Json.Nodes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class NonRestContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] Surfaces =
    [
        "/sessions/{id}/stream",
        "/api/events",
        "/api/programs/bulk",
        "/api/videos/{id}",
    ];

    [Fact]
    public async Task TheDocumentNamesTheContractsItCannotHold()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        string description = document["info"]!["description"]!.GetValue<string>();
        string[] described = document["paths"]!.AsObject().Select(path => path.Key).ToArray();

        foreach (string surface in Surfaces)
        {
            Assert.Contains(surface, description, StringComparison.Ordinal);
            Assert.DoesNotContain(surface, described, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task TheOneThatHandsARecordingOverSaysItIsForAPlayerOutsideTheBrowser()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        string description = document["info"]!["description"]!.GetValue<string>();

        Assert.Contains("external player", description, StringComparison.Ordinal);
        Assert.Contains(
            "is not the path a browser plays a recording through",
            description,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/api/videos",
            document["paths"]!.AsObject().Select(path => path.Key),
            StringComparer.Ordinal);
    }
}
