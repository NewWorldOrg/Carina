using System.Net;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

internal static class ServedOpenApi
{
    public const string Route = "/openapi/v1.json";

    public const string DeclarationFile = "openapi/non-rest-contracts.md";

    public static async Task<JsonNode> FetchAsync(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var client = factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(new Uri(Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    public static IEnumerable<(string Path, string Method, JsonObject Value)> Operations(
        JsonNode document
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        return document["paths"]!
            .AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation =>
                (path.Key, operation.Key, operation.Value!.AsObject())));
    }

    public static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Carina.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"No directory above {AppContext.BaseDirectory} holds Carina.slnx."
            );
        }

        return Path.Combine(directory.FullName, relativePath);
    }
}
