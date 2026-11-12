namespace Carina.Conventions.Tests;

public sealed class TestLayoutRuleSelfCheckTests
{
    private const string ApiTests = "tests/Carina.Api.Tests";

    private const string DomainTests = "tests/Carina.Domain.Tests";

    private const string InfrastructureTests = "tests/Carina.Infrastructure.Tests";

    private const string DbTests = "tests/Carina.Db.Tests";

    private const string Controllers = "src/Carina.Api/Controllers";

    private static readonly IReadOnlyDictionary<string, string> ApiFolders =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Authentication"] = "Auth" };

    [Fact]
    public void ListsOnlyTheFilesThatHoldATestClassInAFolderOfTheLayout()
    {
        using SourceTree tree = new();
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.Unit);
        tree.Test($"{ApiTests}/DbIntegration/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/DbIntegration/Infra/Auth/LocalAccountBootstrapTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/Unit/Infra/Auth/PasswordHasherTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/AuthSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/Unit/Infra/Auth/AuthenticationMigrationTests.cs", TestLayoutRules.Unit);
        tree.Declare($"{ApiTests}/Feature/Auth/SignedInClient.cs");
        tree.Test($"{ApiTests}/FeatureTest/HealthEndpointTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/ClockTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/RecordingSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test("tests/Carina.Architecture.Tests/Unit/Auth/LayerTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/DbIntegration/App/Auth/AuthSessionServiceTests.cs",
                "/tests/Carina.Api.Tests/Feature/Auth/LogInActionTests.cs",
                "/tests/Carina.Api.Tests/Unit/App/Auth/AuthSessionServiceTests.cs",
                "/tests/Carina.Db.Tests/DbIntegration/Infra/Auth/AuthSchemaTests.cs",
                "/tests/Carina.Db.Tests/Unit/Infra/Auth/AuthenticationMigrationTests.cs",
                "/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsTests.cs",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Infra/Auth/LocalAccountBootstrapTests.cs",
                "/tests/Carina.Infrastructure.Tests/Unit/Infra/Auth/PasswordHasherTests.cs",
            ],
            TestLayoutRules.FilesInTheLayout(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassUnderAnAreaTheProductCodeDoesNotHave()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Test($"{DomainTests}/Unit/Login/OidcSettingsTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            ["/tests/Carina.Domain.Tests/Unit/Login/OidcSettingsTests.cs:4 OidcSettingsTests is under Login, which is no area of the product code"],
            TestLayoutRules.ClassesOutOfPlace(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassUnderAKindFolderThatIsNoPlaceOfTheLayout()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Test($"{ApiTests}/Feature/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/Domain/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/Feature/Auth/AuthSchemaTests.cs", TestLayoutRules.Feature);
        tree.Test($"{DomainTests}/DbIntegration/Auth/OidcSettingsTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DomainTests}/Unit/OidcSettingsTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/Feature/LogInActionTests.cs:4 LogInActionTests sits in no folder of the layout",
                "/tests/Carina.Api.Tests/Unit/Domain/Auth/OidcSettingsTests.cs:4 OidcSettingsTests sits in no folder of the layout",
                "/tests/Carina.Db.Tests/Feature/Auth/AuthSchemaTests.cs:4 AuthSchemaTests sits in no folder of the layout",
                "/tests/Carina.Domain.Tests/DbIntegration/Auth/OidcSettingsTests.cs:4 OidcSettingsTests sits in no folder of the layout",
                "/tests/Carina.Domain.Tests/Unit/OidcSettingsTests.cs:4 OidcSettingsTests sits in no folder of the layout",
            ],
            TestLayoutRules.ClassesOutOfPlace(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassOutsideTheNamespaceOfItsFolder()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Write(
            $"{DomainTests}/Unit/Auth/OidcSettingsTests.cs",
            """
            namespace Carina.Domain.Tests.Auth;

            [Trait("Category", "Unit")]
            public sealed class OidcSettingsTests
            {
                [Fact]
                public void Holds()
                {
                }
            }
            """);

        Assert.Equal(
            ["/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsTests.cs:4 OidcSettingsTests is in the namespace Carina.Domain.Tests.Auth, not Carina.Domain.Tests.Unit.Auth"],
            TestLayoutRules.ClassesOutOfPlace(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassThatIsNotInAFileOfItsOwnName()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Write(
            $"{DomainTests}/Unit/Auth/OidcSettingsTests.cs",
            """
            namespace Carina.Domain.Tests.Unit.Auth;

            [Trait("Category", "Unit")]
            public sealed class OidcSettingsTests
            {
                [Fact]
                public void Holds()
                {
                }
            }

            [Trait("Category", "Unit")]
            public sealed class OidcIssuerTests
            {
                [Theory]
                [InlineData(1)]
                public void Holds(int one)
                {
                }
            }
            """);

        Assert.Equal(
            ["/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsTests.cs:13 OidcIssuerTests is not in a file of its own name"],
            TestLayoutRules.ClassesOutOfPlace(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffATestClassInItsPlaceAndWhateverSitsInAnOldPlace()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Declare($"{Controllers}/Epg/GetProgrammesAction.cs");
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/Sessions/SessionTokenTests.cs", TestLayoutRules.Unit);
        tree.Test($"{ApiTests}/Feature/Epg/GetProgrammesActionTests.cs", TestLayoutRules.Feature);
        tree.Declare($"{DomainTests}/Unit/Auth/SampleSettings.cs");
        tree.Write($"{DomainTests}/Recordings/RecordingTests.cs", Source("Carina.Domain.Tests", "RecordingChecks", TestLayoutRules.Unit));
        tree.Write($"{DomainTests}/ClockTests.cs", Source("Carina.Domain.Tests.Time", "ClockChecks", TestLayoutRules.Unit));
        tree.Write($"{ApiTests}/FeatureTest/StartUpTests.cs", Source("Carina.Api.Tests", "StartUpChecks", TestLayoutRules.Feature));
        tree.Write($"{ApiTests}/Unit/ClockTests.cs", Source("Carina.Api.Tests", "ClockChecks", TestLayoutRules.Unit));

        Assert.Empty(TestLayoutRules.ClassesOutOfPlace(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassThatDoesNotCarryTheCategoryOfItsKind()
    {
        using SourceTree tree = new();
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Unit);
        tree.Write(
            $"{DomainTests}/Unit/Auth/OidcSettingsTests.cs",
            """
            namespace Carina.Domain.Tests.Unit.Auth;

            public sealed class OidcSettingsTests
            {
                [Fact]
                [Trait("Category", "Unit")]
                public void Holds()
                {
                }
            }
            """);
        tree.Write(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs",
            """
            namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Auth;

            [Trait("Category", "DbIntegration")]
            [Trait("Category", "Scale")]
            public sealed class AuthSessionRepositoryTests
            {
                [Fact]
                public void Holds()
                {
                }
            }
            """);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/Feature/Auth/LogInActionTests.cs:4 LogInActionTests is a Feature test but carries Unit",
                "/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsTests.cs:3 OidcSettingsTests is a Unit test but carries no category",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:5 AuthSessionRepositoryTests is a DbIntegration test but carries DbIntegration, Scale",
            ],
            TestLayoutRules.ClassesUnderAnotherCategory(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffTheCategoryOfTheKindAndTheScaleAndMaterialCategories()
    {
        using SourceTree tree = new();
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/AuthSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DomainTests}/Unit/Auth/SessionTokenScaleTests.cs", "Scale");
        tree.Test($"{InfrastructureTests}/Unit/Infra/Auth/PasswordHasherMaterialTests.cs", "Material");
        tree.Write(
            $"{DomainTests}/Unit/Auth/OidcSettingsTests.cs",
            """
            namespace Carina.Domain.Tests.Unit.Auth;

            [Trait("Speed", "Slow")]
            [Trait("Category", "Unit")]
            public sealed class OidcSettingsTests
            {
                [Fact]
                public void Holds()
                {
                }
            }
            """);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", "Anything");

        Assert.Empty(TestLayoutRules.ClassesUnderAnotherCategory(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsAFeatureClassNamedAfterNoActionOfItsAreaAndNoClassTheStartUpCodeNames()
    {
        using SourceTree tree = new();
        tree.Declare($"{Controllers}/Recordings/GetRecordingAction.cs");
        tree.Declare("src/Carina.Api/Authentication/DeviceLabel.cs");
        tree.Write("src/Carina.Api/Program.cs", "builder.Services.AddSingleton<SessionCookiePolicy>();");
        tree.Test($"{ApiTests}/Feature/Auth/DeviceLabelTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Auth/GetRecordingActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Auth/SignInJourneyTests.cs", TestLayoutRules.Feature);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/Feature/Auth/DeviceLabelTests.cs:4 DeviceLabelTests is named after nothing the API maps: no action of Auth, no class the start-up code names",
                "/tests/Carina.Api.Tests/Feature/Auth/GetRecordingActionTests.cs:4 GetRecordingActionTests is named after nothing the API maps: no action of Auth, no class the start-up code names",
                "/tests/Carina.Api.Tests/Feature/Auth/SignInJourneyTests.cs:4 SignInJourneyTests is named after nothing the API maps: no action of Auth, no class the start-up code names",
            ],
            TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffAFeatureClassNamedAfterAnActionOfItsAreaOrAClassTheStartUpCodeNames()
    {
        using SourceTree tree = new();
        tree.Declare($"{Controllers}/Auth/LogInAction.cs");
        tree.Declare("src/Carina.Api/Authentication/SessionCookiePolicy.cs");
        tree.Declare("src/Carina.Api/Authentication/TrustedProxyDiagnosis.cs");
        tree.Write("src/Carina.Api/Program.cs", "builder.Services.AddSingleton<SessionCookiePolicy>();");
        tree.Write(
            "src/Carina.Api/Extensions/ServiceCollectionExtensions.cs",
            """
            namespace Carina.Api.Extensions;

            public static class ServiceCollectionExtensions
            {
                public static void AddDiagnosis(this IServiceCollection services)
                    => services.AddHostedService<TrustedProxyDiagnosis>();
            }
            """);
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Auth/SessionCookiePolicyTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Auth/TrustedProxyDiagnosisTests.cs", TestLayoutRules.Feature);

        Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassNamedAfterATypeOfAnotherLayer()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Declare("src/Carina.Api/Services/AuthSessionService.cs");
        tree.Declare("src/Carina.Infrastructure/Auth/PasswordHasher.cs");
        tree.Test($"{ApiTests}/DbIntegration/App/Auth/PasswordHasherTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{ApiTests}/Unit/App/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/AuthSessionServiceTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/DbIntegration/Infra/Auth/AuthSessionServiceTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/Unit/Infra/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/DbIntegration/App/Auth/PasswordHasherTests.cs:4 PasswordHasherTests is named after nothing the API declares",
                "/tests/Carina.Api.Tests/Unit/App/Auth/OidcSettingsTests.cs:4 OidcSettingsTests is named after nothing the API declares",
                "/tests/Carina.Domain.Tests/Unit/Auth/AuthSessionServiceTests.cs:4 AuthSessionServiceTests is named after nothing the domain declares",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Infra/Auth/AuthSessionServiceTests.cs:4 AuthSessionServiceTests is named after nothing the infrastructure declares",
                "/tests/Carina.Infrastructure.Tests/Unit/Infra/Auth/OidcSettingsTests.cs:4 OidcSettingsTests is named after nothing the infrastructure declares",
            ],
            TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassWhoseNameDoesNotEndInTests()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettings.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsChecks.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Domain.Tests/Unit/Auth/OidcSettings.cs:4 OidcSettings is named after nothing the domain declares",
                "/tests/Carina.Domain.Tests/Unit/Auth/OidcSettingsChecks.cs:4 OidcSettingsChecks is named after nothing the domain declares",
            ],
            TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffATestClassNamedAfterATypeOfItsLayer()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Write(
            "src/Carina.Domain/Auth/SessionToken.cs",
            """
            namespace Carina.Domain.Auth;

            public readonly record struct SessionToken(string Value)
            {
                public enum Strength
                {
                    Weak,
                }
            }
            """);
        tree.Declare("src/Carina.Api/Services/AuthSessionService.cs");
        tree.Declare("src/Carina.Infrastructure/Auth/PasswordHasher.cs");
        tree.Test($"{ApiTests}/DbIntegration/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{ApiTests}/Unit/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/SessionTokenTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/StrengthTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/DbIntegration/Infra/Auth/PasswordHasherTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/Unit/Infra/Auth/PasswordHasherTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Recordings/NothingAtAllTests.cs", TestLayoutRules.Unit);

        Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestOverADatabaseNamedAfterNoRepositoryBehindADomainInterfaceAndNoDomainService()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Infrastructure/Persistence/Repositories/AuthSessionRepository.cs");
        tree.Declare("src/Carina.Infrastructure/Auth/SessionExpiryDomainService.cs");
        tree.Test($"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs", TestLayoutRules.DbIntegration);

        Assert.Equal(
            [
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests is named after nothing the domain declares: no repository behind its interface, no domain service",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs:4 SessionExpiryDomainServiceTests is named after nothing the domain declares: no repository behind its interface, no domain service",
            ],
            TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffATestOverADatabaseNamedAfterARepositoryBehindADomainInterfaceOrADomainService()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/IAuthSessionRepository.cs");
        tree.Declare("src/Carina.Domain/Auth/SessionExpiryDomainService.cs");
        tree.Declare("src/Carina.Infrastructure/Persistence/Repositories/AuthSessionRepository.cs");
        tree.Test($"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{InfrastructureTests}/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs", TestLayoutRules.DbIntegration);

        Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsADatabaseProjectTestNamedAfterNoTypeNoMigrationAndNoSchema()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Db/MigrationLock.cs");
        tree.Declare("src/Carina.Db/Migrations/20260101000000_SessionsAreKeptAsHashes.cs");
        tree.Declare("src/Carina.Db/Migrations/20260101000000_SessionsAreKeptAsHashes.Designer.cs");
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/SessionTableTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/SessionsAreHashedMigrationTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/DesignerMigrationTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/Unit/Infra/Auth/SessionsAreKeptAsHashesTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Db.Tests/DbIntegration/Infra/Auth/DesignerMigrationTests.cs:4 DesignerMigrationTests is named after nothing the database project declares: no type, no migration, no schema",
                "/tests/Carina.Db.Tests/DbIntegration/Infra/Auth/SessionTableTests.cs:4 SessionTableTests is named after nothing the database project declares: no type, no migration, no schema",
                "/tests/Carina.Db.Tests/DbIntegration/Infra/Auth/SessionsAreHashedMigrationTests.cs:4 SessionsAreHashedMigrationTests is named after nothing the database project declares: no type, no migration, no schema",
                "/tests/Carina.Db.Tests/Unit/Infra/Auth/SessionsAreKeptAsHashesTests.cs:4 SessionsAreKeptAsHashesTests is named after nothing the database project declares: no type, no migration, no schema",
            ],
            TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffADatabaseProjectTestNamedAfterATypeAMigrationOrASchema()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Db/MigrationLock.cs");
        tree.Declare("src/Carina.Db/Migrations/20260101000000_SessionsAreKeptAsHashes.cs");
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/AuthSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/DbIntegration/Infra/Auth/SessionsAreKeptAsHashesMigrationTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DbTests}/Unit/Infra/Auth/MigrationLockTests.cs", TestLayoutRules.Unit);

        Assert.Empty(TestLayoutRules.ClassesNamedAfterNothing(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsAnActionWithoutAFeatureClassInAnAreaThatHasFeatureClasses()
    {
        using SourceTree tree = new();
        tree.Declare($"{Controllers}/Auth/LogInAction.cs");
        tree.Declare($"{Controllers}/Auth/LogOutAction.cs");
        tree.Declare($"{Controllers}/Recordings/GetRecordingAction.cs");
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);

        Assert.Equal(
            ["/src/Carina.Api/Controllers/Auth/LogOutAction.cs LogOutAction"],
            TestLayoutRules.ActionsWithoutAFeatureClass(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsAnActionWhoseFeatureClassSitsUnderAnotherArea()
    {
        using SourceTree tree = new();
        tree.Declare($"{Controllers}/Auth/LogInAction.cs");
        tree.Declare($"{Controllers}/Auth/LogOutAction.cs");
        tree.Declare($"{Controllers}/Recordings/GetRecordingAction.cs");
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Recordings/GetRecordingActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Recordings/LogOutActionTests.cs", TestLayoutRules.Feature);

        Assert.Equal(
            ["/src/Carina.Api/Controllers/Auth/LogOutAction.cs LogOutAction"],
            TestLayoutRules.ActionsWithoutAFeatureClass(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffTheActionsOfAnAreaWithoutFeatureClassesInTheLayout()
    {
        using SourceTree tree = new();
        tree.Declare($"{Controllers}/Auth/LogInAction.cs");
        tree.Declare($"{Controllers}/Auth/LogOutAction.cs");
        tree.Declare("src/Carina.Domain/Auth/SignInAction.cs");
        tree.Test($"{ApiTests}/FeatureTest/LoginTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/App/Auth/AuthSessionServiceTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Unit/Auth/SignInActionTests.cs", TestLayoutRules.Unit);

        Assert.Empty(TestLayoutRules.ActionsWithoutAFeatureClass(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsTwoFeatureClassesNamedAfterTheSameSubject()
    {
        using SourceTree tree = new();
        tree.Test($"{ApiTests}/Feature/Auth/SessionCookiePolicyTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Live/SessionCookiePolicyTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Feature/Live/GetLiveActionTests.cs", TestLayoutRules.Feature);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/Feature/Auth/SessionCookiePolicyTests.cs SessionCookiePolicyTests",
                "/tests/Carina.Api.Tests/Feature/Live/SessionCookiePolicyTests.cs SessionCookiePolicyTests",
            ],
            TestLayoutRules.FeatureClassesSharingASubject(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffAFeatureClassAndAUnitClassNamedAfterTheSameSubject()
    {
        using SourceTree tree = new();
        tree.Test($"{ApiTests}/Feature/Auth/SessionCookiePolicyTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/App/Auth/SessionCookiePolicyTests.cs", TestLayoutRules.Unit);
        tree.Test($"{ApiTests}/FeatureTest/SessionCookiePolicyTests.cs", TestLayoutRules.Feature);

        Assert.Empty(TestLayoutRules.FeatureClassesSharingASubject(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsARepositoryBuiltByHandOverADatabase()
    {
        using SourceTree tree = new();
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs",
            TestLayoutRules.DbIntegration,
            "IAuthSessionRepository sessions = new AuthSessionRepository(context);");

        Assert.Equal(
            [
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests builds AuthSessionRepository by hand",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests does not take IAuthSessionRepository from the container",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests names the class AuthSessionRepository behind IAuthSessionRepository",
            ],
            TestLayoutRules.SubjectsBuiltByHand(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsARepositoryTakenFromTheContainerAndThenNamedByItsClass()
    {
        using SourceTree tree = new();
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs",
            TestLayoutRules.DbIntegration,
            "Assert.IsType<AuthSessionRepository>(scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>());");

        Assert.Equal(
            ["/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests names the class AuthSessionRepository behind IAuthSessionRepository"],
            TestLayoutRules.SubjectsBuiltByHand(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsADomainServiceOrAnApplicationServiceBuiltByHandOverADatabase()
    {
        using SourceTree tree = new();
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs",
            TestLayoutRules.DbIntegration,
            "SessionExpiryDomainService service = new(sessions);");
        tree.Test(
            $"{ApiTests}/DbIntegration/App/Auth/AuthSessionServiceTests.cs",
            TestLayoutRules.DbIntegration,
            "object service = new Carina.Api.Services.AuthSessionService(sessions);");

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/DbIntegration/App/Auth/AuthSessionServiceTests.cs:4 AuthSessionServiceTests builds AuthSessionService by hand",
                "/tests/Carina.Api.Tests/DbIntegration/App/Auth/AuthSessionServiceTests.cs:4 AuthSessionServiceTests does not take AuthSessionService from the container",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs:4 SessionExpiryDomainServiceTests builds SessionExpiryDomainService by hand",
                "/tests/Carina.Infrastructure.Tests/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs:4 SessionExpiryDomainServiceTests does not take SessionExpiryDomainService from the container",
            ],
            TestLayoutRules.SubjectsBuiltByHand(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void LetsOffASubjectTakenFromTheContainerAndASubjectBuiltByHandWhereNoContainerIsAsked()
    {
        using SourceTree tree = new();
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/AuthSessionRepositoryTests.cs",
            TestLayoutRules.DbIntegration,
            "IAuthSessionRepository sessions = scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>();");
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Domain/Auth/SessionExpiryDomainServiceTests.cs",
            TestLayoutRules.DbIntegration,
            "SessionExpiryDomainService service = scope.ServiceProvider.GetRequiredService<SessionExpiryDomainService>();");
        tree.Test(
            $"{ApiTests}/DbIntegration/App/Auth/AuthSessionServiceTests.cs",
            TestLayoutRules.DbIntegration,
            "AuthSessionService service = factory.Services.GetRequiredService<Carina.Api.Services.AuthSessionService>();");
        tree.Test(
            $"{ApiTests}/Unit/App/Auth/AuthSessionServiceTests.cs",
            TestLayoutRules.Unit,
            "AuthSessionService service = new(sessions);");
        tree.Test(
            $"{InfrastructureTests}/DbIntegration/Infra/Auth/LocalAccountBootstrapTests.cs",
            TestLayoutRules.DbIntegration,
            "LocalAccountBootstrap bootstrap = new LocalAccountBootstrap(accounts);");
        tree.Test(
            $"{InfrastructureTests}/Auth/AuthSessionRepositoryTests.cs",
            TestLayoutRules.DbIntegration,
            "IAuthSessionRepository sessions = new AuthSessionRepository(context);");

        Assert.Empty(TestLayoutRules.SubjectsBuiltByHand(TestLayoutRules.Read(tree.Root)));
    }

    [Fact]
    public void DetectsATestClassUnderAnOldFolderOfAnAreaThatIsNotListed()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{DomainTests}/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/Auth/Sessions/SessionStoreTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "/tests/Carina.Domain.Tests/Auth/OidcSettingsTests.cs:4 OidcSettingsTests is under the old folder Auth, which is not listed as yet to move",
                "/tests/Carina.Infrastructure.Tests/Auth/Sessions/SessionStoreTests.cs:4 SessionStoreTests is under the old folder Auth, which is not listed as yet to move",
            ],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void DetectsATestClassInAFlatOldPlaceNamedAfterATypeOfAnAreaThatHasMoved()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{ApiTests}/FeatureTest/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test($"{ApiTests}/Unit/SessionCookiePolicyTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/AuthSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{DomainTests}/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/AuthSessionRepositoryTests.cs", TestLayoutRules.DbIntegration);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/FeatureTest/LogInActionTests.cs:4 LogInActionTests is named after a type of Auth, which has moved",
                "/tests/Carina.Api.Tests/Unit/SessionCookiePolicyTests.cs:4 SessionCookiePolicyTests is named after a type of Auth, which has moved",
                "/tests/Carina.Db.Tests/AuthSchemaTests.cs:4 AuthSchemaTests is named after a type of Auth, which has moved",
                "/tests/Carina.Domain.Tests/OidcSettingsTests.cs:4 OidcSettingsTests is named after a type of Auth, which has moved",
                "/tests/Carina.Infrastructure.Tests/AuthSessionRepositoryTests.cs:4 AuthSessionRepositoryTests is named after a type of Auth, which has moved",
            ],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void DetectsAnOldTestOfTheHttpSurfaceThatSpeaksOnlyToRoutesOfAreasThatHaveMoved()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test(
            $"{ApiTests}/FeatureTest/SignInJourneyTests.cs",
            TestLayoutRules.Feature,
            """await client.PostAsync("/api/auth/login", null); await client.DeleteAsync($"/api/auth/sessions/{id}?all=true");""");

        Assert.Equal(
            ["/tests/Carina.Api.Tests/FeatureTest/SignInJourneyTests.cs:4 SignInJourneyTests speaks only to routes of areas that have moved"],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void DetectsATestClassInAFlatOldPlaceWithANamesakeInTheLayout()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{ApiTests}/Unit/App/Auth/DeviceLabelTests.cs", TestLayoutRules.Unit);
        tree.Test($"{ApiTests}/Unit/DeviceLabelTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            ["/tests/Carina.Api.Tests/Unit/DeviceLabelTests.cs:4 DeviceLabelTests has a namesake in the layout"],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void ReportsATestClassTiedToAMovedAreaInSeveralWaysOnce()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{ApiTests}/Feature/Auth/LogInActionTests.cs", TestLayoutRules.Feature);
        tree.Test(
            $"{ApiTests}/FeatureTest/LogInActionTests.cs",
            TestLayoutRules.Feature,
            """await client.PostAsync("/api/auth/login", null);""");

        Assert.Equal(
            ["/tests/Carina.Api.Tests/FeatureTest/LogInActionTests.cs:4 LogInActionTests is named after a type of Auth, which has moved"],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void LetsOffWhatBelongsToAnAreaStillListedAndWhatIsTiedToNoArea()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/RecordingTests.cs", TestLayoutRules.Unit);
        tree.Test(
            $"{ApiTests}/FeatureTest/RecordingJourneyTests.cs",
            TestLayoutRules.Feature,
            """await client.PostAsync("/api/auth/login", null); await client.GetAsync($"/api/recordings/{id}");""");
        tree.Test(
            $"{ApiTests}/FeatureTest/UnmappedPathTests.cs",
            TestLayoutRules.Feature,
            """await client.PostAsync("/api/auth/login", null); await client.GetAsync("/api/nothing/here");""");
        tree.Test($"{ApiTests}/FeatureTest/StartUpTests.cs", TestLayoutRules.Feature);
        tree.Test(
            $"{ApiTests}/Unit/ClockTests.cs",
            TestLayoutRules.Unit,
            """string path = "/api/auth/login";""");
        tree.Test($"{ApiTests}/Unit/App/Auth/DeviceLabelTests.cs", TestLayoutRules.Unit);
        tree.Test($"{InfrastructureTests}/DeviceLabelTests.cs", TestLayoutRules.Unit);

        Assert.Empty(TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), ["Persistence", "Recordings"], ApiFolders));
    }

    [Fact]
    public void LetsOffATypeUnderAFolderOfTheApiThatNoAreaIsNamedFor()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{ApiTests}/Unit/SessionCookiePolicyTests.cs", TestLayoutRules.Unit);

        Assert.Empty(TestLayoutRules.ClassesLeftInTheOldPlace(
            TestLayoutRules.Read(tree.Root),
            ["Persistence", "Recordings"],
            new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void ReportsEveryTestClassOutsideTheLayoutOnceNoAreaIsLeftToMove()
    {
        using SourceTree tree = new();
        DeclareAuthAndRecordings(tree);
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/ClockTests.cs", TestLayoutRules.Unit);
        tree.Test($"{ApiTests}/FeatureTest/StartUpTests.cs", TestLayoutRules.Feature);

        Assert.Equal(
            [
                "/tests/Carina.Api.Tests/FeatureTest/StartUpTests.cs:4 StartUpTests is outside the layout, and no area is left to move",
                "/tests/Carina.Domain.Tests/ClockTests.cs:4 ClockTests is outside the layout, and no area is left to move",
                "/tests/Carina.Domain.Tests/Recordings/RecordingTests.cs:4 RecordingTests is under the old folder Recordings, which is not listed as yet to move",
            ],
            TestLayoutRules.ClassesLeftInTheOldPlace(TestLayoutRules.Read(tree.Root), [], ApiFolders));
    }

    [Fact]
    public void DetectsAListedAreaThatIsNoAreaThatHasTestsInTheLayoutOrThatHasNoTestLeftInAnOldPlace()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Declare("src/Carina.Domain/Recordings/Recording.cs");
        tree.Declare("src/Carina.Domain/Quality/QualityScore.cs");
        tree.Test($"{DomainTests}/Unit/Auth/OidcSettingsTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);

        Assert.Equal(
            [
                "Auth already has tests in the layout",
                "Auth has no test left in an old place",
                "Nowhere is no area of the product code",
                "Quality has no test left in an old place",
            ],
            TestLayoutRules.AreasWronglyListed(TestLayoutRules.Read(tree.Root), ["Auth", "Nowhere", "Quality", "Recordings"], ApiFolders));
    }

    [Fact]
    public void LetsOffAListedAreaWithATestLeftInAnOldFolderOrAFlatOldPlace()
    {
        using SourceTree tree = new();
        tree.Declare("src/Carina.Domain/Recordings/Recording.cs");
        tree.Declare("src/Carina.Domain/Quality/QualityScore.cs");
        tree.Declare("src/Carina.Domain/Channels/Channel.cs");
        tree.Declare("src/Carina.Api/Authentication/SessionCookiePolicy.cs");
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Write(
            $"{Controllers}/Live/GetLiveAction.cs",
            """
            namespace Carina.Api.Controllers.Live;

            [Route("api/live/{channel}")]
            public sealed class GetLiveAction;
            """);
        tree.Test($"{DomainTests}/Recordings/RecordingTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DomainTests}/QualityScoreTests.cs", TestLayoutRules.Unit);
        tree.Test($"{DbTests}/ChannelsSchemaTests.cs", TestLayoutRules.DbIntegration);
        tree.Test($"{ApiTests}/Unit/SessionCookiePolicyTests.cs", TestLayoutRules.Unit);
        tree.Test(
            $"{ApiTests}/FeatureTest/WatchingTests.cs",
            TestLayoutRules.Feature,
            """await client.GetAsync($"/api/live/{channel}"); await client.GetAsync("/api/nothing/here");""");

        Assert.Empty(TestLayoutRules.AreasWronglyListed(
            TestLayoutRules.Read(tree.Root),
            ["Auth", "Channels", "Live", "Quality", "Recordings"],
            ApiFolders));
    }

    private static void DeclareAuthAndRecordings(SourceTree tree)
    {
        tree.Declare("src/Carina.Domain/Auth/OidcSettings.cs");
        tree.Write("src/Carina.Domain/Auth/IAuthSessionRepository.cs", "namespace Carina.Domain.Auth;\n\npublic interface IAuthSessionRepository { }\n");
        tree.Declare("src/Carina.Infrastructure/Persistence/Repositories/AuthSessionRepository.cs");
        tree.Declare("src/Carina.Domain/Recordings/Recording.cs");
        tree.Declare("src/Carina.Api/Authentication/SessionCookiePolicy.cs");
        tree.Declare("src/Carina.Api/Common/DeviceLabel.cs");
        tree.Write(
            "src/Carina.Api/Authentication/AuthRoutes.cs",
            """
            namespace Carina.Api.Authentication;

            public static class AuthRoutes
            {
                public const string Session = "api/auth/sessions/{id}";
            }
            """);
        tree.Write(
            $"{Controllers}/Auth/LogInAction.cs",
            """
            namespace Carina.Api.Controllers.Auth;

            [Route("api/auth/login")]
            public sealed class LogInAction;
            """);
        tree.Write(
            $"{Controllers}/Auth/DeleteSessionAction.cs",
            """
            namespace Carina.Api.Controllers.Auth;

            [Route(AuthRoutes.Session)]
            public sealed class DeleteSessionAction;
            """);
        tree.Write(
            $"{Controllers}/Recordings/GetRecordingAction.cs",
            """
            namespace Carina.Api.Controllers.Recordings;

            [Route("api/recordings/{id}")]
            public sealed class GetRecordingAction;
            """);
    }

    private static string Source(string space, string name, string category, string body = "")
        => $$"""
            namespace {{space}};

            [Trait("Category", "{{category}}")]
            public sealed class {{name}}
            {
                [Fact]
                public async Task Holds()
                {
                    {{body}}
                }
            }
            """;

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-test-layout-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Declare(string path)
            => Write(path, $"namespace {SpaceOf(path)};\n\npublic sealed class {NameOf(path)};\n");

        public void Test(string path, string category, string body = "")
            => Write(path, Source(SpaceOf(path), NameOf(path), category, body));

        public void Dispose() => directory.Delete(recursive: true);

        private static string SpaceOf(string path) => string.Join('.', path.Split('/')[1..^1]);

        private static string NameOf(string path) => Path.GetFileName(path).Split('.')[0];
    }
}
