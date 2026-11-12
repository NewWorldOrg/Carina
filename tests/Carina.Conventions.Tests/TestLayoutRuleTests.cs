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
        => Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(
            Repository,
            TestLayoutRules.SourcesOutsideTheFolderOfTheArea,
            TestLayoutRules.SchemasNotNamedAfterTheArea));

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
            TestLayoutRules.SourcesOutsideTheFolderOfTheArea,
            TestLayoutRules.SchemasNotNamedAfterTheArea));

    [Fact(DisplayName = "the list of areas not yet moved names only areas with tests left in an old place and none in the layout")]
    public void TheListOfAreasNotYetMovedNamesOnlyAreasStillToMove()
        => Assert.Empty(TestLayoutRules.AreasWronglyListed(
            Repository,
            TestLayoutRules.AreasNotYetMoved,
            TestLayoutRules.SourcesOutsideTheFolderOfTheArea,
            TestLayoutRules.SchemasNotNamedAfterTheArea));

    [Fact(DisplayName = "the list of what an area keeps outside its own folder names only areas and sources that exist")]
    public void TheListOfSourcesOutsideTheFolderOfTheAreaNamesOnlyWhatExists()
        => Assert.Empty(TestLayoutRules.SourcesWronglyListed(Repository, TestLayoutRules.SourcesOutsideTheFolderOfTheArea));

    [Fact(DisplayName = "the list of schema tests not named after the area names only areas and test classes that exist")]
    public void TheListOfSchemasNotNamedAfterTheAreaNamesOnlyWhatExists()
        => Assert.Empty(TestLayoutRules.SchemasWronglyListed(Repository, TestLayoutRules.SchemasNotNamedAfterTheArea));

    [Fact(DisplayName = "the start-up code is read for what it puts in the request pipeline or maps an endpoint to, not for what it registers")]
    public void TheStartUpCodeIsReadForWhatServesRequestsNotForWhatItRegisters()
    {
        IReadOnlyList<string> classes = TestLayoutRules.ClassesServingRequests(Repository);

        Assert.Contains("DefaultDenyAuthenticationMiddleware", classes);
        Assert.Contains("SessionAuthenticationHandler", classes);
        Assert.Contains("SessionCookiePolicy", classes);
        Assert.Contains("AppEventStream", classes);
        Assert.Contains("RecordingHlsDelivery", classes);
        Assert.DoesNotContain("AuthSessionService", classes);
        Assert.DoesNotContain("TrustedProxyDiagnosis", classes);
        Assert.DoesNotContain("PlaybackService", classes);
    }

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
