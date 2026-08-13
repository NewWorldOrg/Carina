using System.Text.Json.Nodes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class OpenApiArtifactTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task TheCheckedInDocumentIsWhatTheApplicationServes()
    {
        var path = ServedOpenApi.RepositoryFile(ServedOpenApi.DocumentFile);

        Assert.True(
            File.Exists(path),
            $"{ServedOpenApi.DocumentFile} is missing. Generate it with `task openapi`."
        );

        var served = await ServedOpenApi.FetchAsync(factory);
        var checkedIn = JsonNode.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(ServedOpenApi.Canonical(checkedIn), ServedOpenApi.Canonical(served));
    }
}
