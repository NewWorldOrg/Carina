namespace Carina.Conventions.Tests;

public sealed class TestLayoutRuleTests
{
    private static readonly TestLayoutRules.Reading Repository = TestLayoutRules.Read(RepositoryPaths.Root);

    [Fact(DisplayName = "a test class in the layout sits under an area of the product code, alone in a file of its own name, in the namespace of its folder")]
    public void ATestClassInTheLayoutSitsInItsPlace()
        => Assert.Empty(TestLayoutRules.ClassesOutOfPlace(Repository));

    [Fact(DisplayName = "a test class in the layout carries the category of its kind")]
    public void ATestClassInTheLayoutCarriesTheCategoryOfItsKind()
        => Assert.Empty(TestLayoutRules.ClassesUnderAnotherCategory(Repository));

    [Fact(DisplayName = "a test class in the layout is named after what it tests")]
    public void ATestClassInTheLayoutIsNamedAfterWhatItTests()
        => Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(Repository));

    [Fact(DisplayName = "every action of an area that has feature tests has its feature class")]
    public void EveryActionOfAnAreaWithFeatureClassesHasItsFeatureClass()
        => Assert.Empty(TestLayoutRules.ActionsWithoutAFeatureClass(Repository));

    [Fact(DisplayName = "no two feature classes share a subject")]
    public void NoTwoFeatureClassesShareASubject()
        => Assert.Empty(TestLayoutRules.FeatureClassesSharingASubject(Repository));

    [Fact(DisplayName = "a test over a real database takes its subject from the container")]
    public void ATestOverARealDatabaseTakesItsSubjectFromTheContainer()
        => Assert.Empty(TestLayoutRules.SubjectsBuiltByHand(Repository));

    [Fact(DisplayName = "no test of an area that has moved is left in an old place")]
    public void NoTestOfAnAreaThatHasMovedIsLeftInAnOldPlace()
        => Assert.Empty(TestLayoutRules.ClassesLeftInTheOldPlace(
            Repository,
            TestLayoutRules.AreasNotYetMoved,
            TestLayoutRules.ApiFoldersOfAnArea));

    [Fact(DisplayName = "the list of areas not yet moved names only areas with tests left in an old place and none in the layout")]
    public void TheListOfAreasNotYetMovedNamesOnlyAreasStillToMove()
        => Assert.Empty(TestLayoutRules.AreasWronglyListed(
            Repository,
            TestLayoutRules.AreasNotYetMoved,
            TestLayoutRules.ApiFoldersOfAnArea));

    [Fact(DisplayName = "the rules have test classes in the layout to bite on")]
    public void TheRulesHaveTestClassesInTheLayoutToBiteOn()
    {
        IReadOnlyList<string> files = TestLayoutRules.FilesInTheLayout(Repository);

        Assert.Contains("/tests/Carina.Api.Tests/Feature/Auth/LogInActionTests.cs", files);
        Assert.Contains("/tests/Carina.Api.Tests/Unit/App/Auth/AuthSessionServiceTests.cs", files);
        Assert.Contains("/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsTests.cs", files);
        Assert.Contains("/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs", files);
        Assert.Contains("/tests/Carina.Db.Tests/DbIntegration/Infra/Auth/AuthSchemaTests.cs", files);
    }
}
