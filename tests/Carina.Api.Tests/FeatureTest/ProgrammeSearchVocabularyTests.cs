using System.Reflection;
using System.Text.Json.Nodes;

using Carina.Api.Controllers.Epg;
using Carina.Infrastructure.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProgrammeSearchVocabularyTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task TheDocumentNamesEveryWordOfTheSearchVocabularyAndNothingBeside()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonArray declared = document["paths"]!["/api/programs/search"]!["get"]!["parameters"]!.AsArray();

        Assert.Equal(
            [.. ProgrammeSearchQuery.Vocabulary.Select(term => term.Name).Order(StringComparer.Ordinal)],
            [.. declared.Select(parameter => parameter!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)]);
        Assert.All(declared, parameter => Assert.Equal("query", parameter!["in"]!.GetValue<string>()));
    }

    [Fact]
    public void TheActionSpellsNoneOfTheVocabularyItself()
    {
        MethodInfo invoke = typeof(SearchProgrammesAction).GetMethod(
            nameof(SearchProgrammesAction.Invoke),
            BindingFlags.Public | BindingFlags.Instance)!;

        Assert.Equal(
            [typeof(CancellationToken)],
            invoke.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
