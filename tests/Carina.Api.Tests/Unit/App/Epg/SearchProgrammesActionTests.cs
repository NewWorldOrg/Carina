using System.Reflection;

using Carina.Api.Controllers.Epg;

namespace Carina.Api.Tests.Unit.App.Epg;

[Trait("Category", "Unit")]
public sealed class SearchProgrammesActionTests
{
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
