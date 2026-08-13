using Carina.Contracts;

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

        Assert.Contains(ServedOpenApi.DeclarationFile, description, StringComparison.Ordinal);

        foreach (var surface in Surfaces)
        {
            Assert.Contains(surface, description, StringComparison.Ordinal);
            Assert.DoesNotContain(surface, described, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void EachSurfaceIsDeclaredInTheRepository()
    {
        var declaration = Declaration();

        foreach (var surface in Surfaces)
        {
            Assert.Contains(surface, declaration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheEventHubDeclarationCarriesEveryNameTheContractsHold()
    {
        var declaration = Declaration();

        var undeclared = AppEvents.All
            .Where(name => !declaration.Contains($"`{name}`", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(undeclared);
    }

    private static string Declaration()
    {
        var path = ServedOpenApi.RepositoryFile(ServedOpenApi.DeclarationFile);

        Assert.True(File.Exists(path), $"{ServedOpenApi.DeclarationFile} is missing.");

        return File.ReadAllText(path);
    }
}
