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
    ];

    [Fact]
    public async Task TheDocumentNamesTheContractsItCannotHold()
    {
        var document = await ServedOpenApi.FetchAsync(factory);
        var description = document["info"]!["description"]!.GetValue<string>();
        var described = document["paths"]!.AsObject().Select(path => path.Key).ToArray();

        foreach (var surface in Surfaces)
        {
            Assert.Contains(surface, description, StringComparison.Ordinal);
            Assert.DoesNotContain(surface, described, StringComparer.Ordinal);
        }
    }
}
