using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// Holds the layout CLAUDE.md gives under "Tests": the folder a test class sits in, the subject it is named
/// after and the category it carries. <see cref="AreasNotYetMoved"/> names the areas whose tests may still
/// be in an old place. The rules read the syntax tree and no build output.
/// </summary>
public static class TestLayoutRules
{
    public const string Feature = "Feature";

    public const string Unit = "Unit";

    public const string DbIntegration = "DbIntegration";

    public static readonly IReadOnlyList<string> AreasNotYetMoved =
    [
        "Base",
        "Configuration",
        "DependencyInjection",
        "Events",
        "Health",
        "Net",
        "Persistence",
        "Services",
        "Version",
    ];

    /// <summary>
    /// What an area keeps outside the folder of its own name: a folder, written with a closing slash, or one file.
    /// </summary>
    public static readonly IReadOnlyList<AreaSource> SourcesOutsideTheFolderOfTheArea =
    [
        new("Auth", "src/Carina.Api/Authentication/"),
        new("Auth", "src/Carina.Api/Services/AuthSessionService.cs"),
        new("Auth", "src/Carina.Api/Services/LocalAccountService.cs"),
        new("Auth", "src/Carina.Db/Migrations/20260819063247_Authentication.cs"),
        new("Auth", "src/Carina.Db/Migrations/20260905022019_SessionDisplayName.cs"),
        new("Auth", "src/Carina.Db/Migrations/20260926141610_SessionsAreKeptAsHashes.cs"),
        new("Auth", "src/Carina.Db/Migrations/20260929160610_TheClientSecretIsSealed.cs"),
        new("Auth", "src/Carina.Infrastructure/Configuration/AuthOptions.cs"),
        new("Auth", "src/Carina.Infrastructure/Persistence/Repositories/OidcSettingsRepository.cs"),
        new("Captions", "src/Carina.Api/Playback/CaptionDelivery.cs"),
        new("DataBroadcast", "src/Carina.Api/Playback/DataBroadcastDelivery.cs"),
        new("DataBroadcast", "src/Carina.Db/Migrations/20261010124242_ADataBroadcastStateForEveryRecording.cs"),
        new("DriverStatus", "src/Carina.Api/Services/DriverStatusService.cs"),
        new("Encoding", "src/Carina.Api/Services/EncodeRerun.cs"),
        new("Encodings", "src/Carina.Db/Migrations/20260914061902_WhereTheBreaksWereJudgedToBe.cs"),
        new("Encodings", "src/Carina.Db/Migrations/20260927053702_ReplacedArtefactsAreRemoved.cs"),
        new("Encodings", "src/Carina.Db/Migrations/20261018102824_TheChaptersOfAnArtefactSayWhichDetectionTheyCameFrom.cs"),
        new("Encodings", "src/Carina.Db/Migrations/20261019091700_ChaptersOfAnArtefactTellOpeningsAndEndingsApart.cs"),
        new("Encodings", "src/Carina.Infrastructure/Configuration/EncodingOptions.cs"),
        new("Epg", "src/Carina.Api/Events/ProgrammeFeedReaders.cs"),
        new("Epg", "src/Carina.Api/Events/ProgrammeFeedStream.cs"),
        new("Integrity", "src/Carina.Api/Services/IntegrityService.cs"),
        new("Integrity", "src/Carina.Db/Migrations/20260914005111_AFindingNamedForTheFactItIsAbout.cs"),
        new("Integrity", "src/Carina.Infrastructure/Configuration/IntegrityOptions.cs"),
        new("Live", "src/Carina.Api/Live/HybridcastAppDelivery.cs"),
        new("Live", "src/Carina.Api/Live/HybridcastSocketDelivery.cs"),
        new("Logos", "src/Carina.Api/Logos/LogoDelivery.cs"),
        new("Migration", "src/Carina.Db/CarryArguments.cs"),
        new("Migration", "src/Carina.Db/CarrySaid.cs"),
        new("Notifications", "src/Carina.Api/Requests/NotificationRequests.cs"),
        new("Notifications", "src/Carina.Api/Services/NotificationDestinationService.cs"),
        new("Notifications", "src/Carina.Api/Services/NotificationPushService.cs"),
        new("Notifications", "src/Carina.Api/Services/NotificationRuleService.cs"),
        new("Notifications", "src/Carina.Db/Migrations/20261110182416_AProgrammeCanBeWatchedWithoutBeingRecorded.cs"),
        new("Notifications", "src/Carina.Db/Migrations/20261112182442_ANoticeCanBeSentToABrowser.cs"),
        new("Playback", "src/Carina.Api/Playback/AskedDecoding.cs"),
        new("Playback", "src/Carina.Api/Playback/AskedFormat.cs"),
        new("Playback", "src/Carina.Api/Playback/ByteRange.cs"),
        new("Playback", "src/Carina.Api/Playback/PlayDelivery.cs"),
        new("Playback", "src/Carina.Api/Playback/RecordingHlsDelivery.cs"),
        new("Playback", "src/Carina.Api/Playback/VideoDelivery.cs"),
        new("Playback", "src/Carina.Api/Responder/Playback/PlaybackPlanResponder.cs"),
        new("Programmes", "src/Carina.Db/Migrations/20260821083911_ProgrammeSearchNormalisation.cs"),
        new("Programmes", "src/Carina.Db/Migrations/20260821090203_ArchivedProgrammeSearchColumns.cs"),
        new("Programmes", "src/Carina.Db/Migrations/20260913150647_ThePictureABroadcastAnnounces.cs"),
        new("Programmes", "src/Carina.Db/Migrations/20261006104532_AProgrammeCarriesTheMarksOfItsNameAndSummary.cs"),
        new("Programmes", "src/Carina.Db/Migrations/20261014092811_TheArchiveKeepsWhichProgrammesWereSuperseded.cs"),
        new("Quality", "src/Carina.Api/Services/QualityThresholdService.cs"),
        new("Quality", "src/Carina.Db/Migrations/20260915090724_AnAnomalyStandsUntilItClears.cs"),
        new("Quality", "src/Carina.Db/Migrations/20261005131056_ASignalLevelCanStandOnAMeasurement.cs"),
        new("Quality", "src/Carina.Infrastructure/Configuration/QualitySignalOptions.cs"),
        new("Recordings", "src/Carina.Api/Common/RecordingIdText.cs"),
        new("Recordings", "src/Carina.Api/Services/RecordingService.cs"),
        new("Recordings", "src/Carina.Db/Migrations/20260915011204_TheEndARecordingWasPromised.cs"),
        new("Recordings", "src/Carina.Infrastructure/Configuration/RecordingRetryOptions.cs"),
        new("Recordings", "src/Carina.Infrastructure/Persistence/Configurations/RecordingGuards.cs"),
        new("Reservations", "src/Carina.Db/Migrations/20260928091011_AReservationStandsAsideForTheSameBroadcast.cs"),
        new("Segments", "src/Carina.Api/Responder/Segments/SegmentStatusResponder.cs"),
        new("Segments", "src/Carina.Api/Services/SegmentStatusService.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261018181106_SkipButtonsAreOfferedFromAThresholdTheSettingsKeep.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261018215453_CatalogEntriesSayWhichVersionOfTheKindEstimationTheyWereEstimatedWith.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261019102212_SegmentsAreNotAnsweredByHand.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261019121457_VersionsWeighNothingTheCatalogHears.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261020091115_CatalogEntriesCarryOnOpeningsAndCopiesOfProgrammesEndWhereTheirRecordingsWerePromised.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261022152742_LearningIsAlwaysOnAndEncodesLearnNoWatermarks.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261023075840_VersionsBuiltInAreMarkedAndThoseNeverUsedLetGo.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261026110822_RoundsOfLearningKeepOutACandidateThatLosesOpeningsOrEndings.cs"),
        new("Segments", "src/Carina.Db/Migrations/20261107141136_TheSkipThresholdReachesNinetyNineHundredths.cs"),
        new("Segments", "src/Carina.Infrastructure/Configuration/LearningOptions.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveHlsDelivery.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveHlsPlaylists.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveRefusalClosures.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveStreamDelivery.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveWire.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveWireRequest.cs"),
        new("Streaming", "src/Carina.Api/Live/LiveWireSocket.cs"),
        new("Thumbnails", "src/Carina.Api/Playback/ScrubDelivery.cs"),
        new("Thumbnails", "src/Carina.Api/Playback/ThumbnailDelivery.cs"),
    ];

    /// <summary>
    /// The schema tests that are not named after the area alone: the area and the name of the class without its ending.
    /// </summary>
    public static readonly IReadOnlyList<AreaSchema> SchemasNotNamedAfterTheArea =
    [
        new("Channels", "LogoVisitSchema"),
        new("Channels", "ServiceReachSchema"),
        new("Channels", "StationLogoSchema"),
        new("DataBroadcast", "DataBroadcastSettingsSchema"),
        new("Migration", "MigrationRecordSchema"),
        new("Programmes", "ProgrammeSearchFoldingSchema"),
        new("Quality", "QualityTablesDroppedWholeSchema"),
        new("Recordings", "RecordingBroadcastGroupSchema"),
        new("Recordings", "RecordingDropPositionSchema"),
        new("Recordings", "RecordingFileNameSchema"),
        new("Recordings", "RecordingJsonShapeSchema"),
        new("Recordings", "RecordingProjectionSchema"),
        new("Recordings", "RecordingUniquenessSchema"),
        new("Reservations", "ReservationOutcomeSchema"),
        new("Segments", "CatalogSchema"),
        new("Segments", "LearningDataSchema"),
        new("Segments", "SeekEventSchema"),
        new("Segments", "SegmentDetectionSchema"),
        new("Segments", "SegmentLearningRoundSchema"),
        new("Segments", "SegmentSettingsSchema"),
        new("Segments", "SkipEventSchema"),
        new("Viewing", "PlaybackPositionSchema"),
    ];

    private const string ApiTests = "Carina.Api.Tests";

    private const string DomainTests = "Carina.Domain.Tests";

    private const string InfrastructureTests = "Carina.Infrastructure.Tests";

    private const string DbTests = "Carina.Db.Tests";

    private const string ApiSource = "/src/Carina.Api/";

    private const string DomainSource = "/src/Carina.Domain/";

    private const string InfrastructureSource = "/src/Carina.Infrastructure/";

    private const string DbSource = "/src/Carina.Db/";

    private const string Controllers = ApiSource + "Controllers/";

    private const string OldFeatureFolder = "FeatureTest";

    private const string Suffix = "Tests";

    private const string Action = "Action";

    private const string Service = "Service";

    private const string Repository = "Repository";

    private const string Migration = "Migration";

    private const string Schema = "Schema";

    private const string Hole = "*";

    private const string MiddlewareCall = "Use";

    private const string EndpointCall = "Map";

    private const string SchemeCall = "AddScheme";

    private const string FilterCall = "Add";

    private const string EndpointFilterCall = "AddEndpointFilter";

    private const string Filters = "Filters";

    private static readonly string[] RequestCalls =
    [
        "GetAsync",
        "PostAsync",
        "PutAsync",
        "PatchAsync",
        "DeleteAsync",
        "SendAsync",
        "GetStringAsync",
        "GetStreamAsync",
        "GetByteArrayAsync",
        "GetFromJsonAsync",
        "PostAsJsonAsync",
        "PutAsJsonAsync",
        "PatchAsJsonAsync",
        "DeleteFromJsonAsync",
    ];

    private static readonly Layer ApiLayer = new(ApiSource, Controllers);

    private static readonly Layer DomainLayer = new(DomainSource, DomainSource);

    private static readonly Layer InfrastructureLayer = new(InfrastructureSource, InfrastructureSource);

    private static readonly string[] Projects = [ApiTests, DomainTests, InfrastructureTests, DbTests];

    private static readonly string[] KindFolders = [Feature, Unit, DbIntegration];

    private static readonly string[] CategoriesOfAnyKind = ["Scale", "Material"];

    private static readonly string[] AreaRoots = ["src/Carina.Domain", "src/Carina.Infrastructure", "src/Carina.Api/Controllers"];

    private static readonly IReadOnlyList<Place> Places =
    [
        new(ApiTests, [Feature], Feature, Subject.Endpoint),
        new(ApiTests, [Unit, "App"], Unit, Subject.ApiType),
        new(ApiTests, [DbIntegration, "App"], DbIntegration, Subject.AppService),
        new(DomainTests, [Unit], Unit, Subject.DomainType),
        new(InfrastructureTests, [DbIntegration, "Domain"], DbIntegration, Subject.DomainPort),
        new(InfrastructureTests, [DbIntegration, "Infra"], DbIntegration, Subject.InfrastructureType),
        new(InfrastructureTests, [Unit, "Infra"], Unit, Subject.InfrastructureType),
        new(DbTests, [DbIntegration, "Infra"], DbIntegration, Subject.DbType),
        new(DbTests, [Unit, "Infra"], Unit, Subject.DbType),
    ];

    internal enum Standing
    {
        InTheLayout,
        InNoFolderOfTheLayout,
        InAnOldFolder,
        InAFlatOldPlace,
    }

    internal enum Subject
    {
        Endpoint,
        ApiType,
        AppService,
        DomainType,
        DomainPort,
        InfrastructureType,
        DbType,
    }

    public static Reading Read(string root)
    {
        ParsedFile[] product = Scanned(root, "src").Select(Parsed).ToArray();
        ILookup<string, string> constants = product
            .SelectMany(file => file.Root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword))
            .SelectMany(Constants)
            .ToLookup(constant => constant.Key, constant => constant.Value, StringComparer.Ordinal);

        ILookup<string, string> migrations = Migrations(root);

        HashSet<string> domainInterfaces = product
            .Where(file => file.Relative.StartsWith(DomainSource, StringComparison.Ordinal))
            .SelectMany(file => file.Root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        return new Reading(
            product.Select(file => file.Relative).Concat(migrations.SelectMany(files => files)).ToHashSet(StringComparer.Ordinal),
            product
                .SelectMany(file => file.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                    .Select(declaration => new Declared(declaration.Identifier.ValueText, file.Relative)))
                .ToLookup(declared => declared.Name, declared => declared.Relative, StringComparer.Ordinal),
            migrations,
            product.Where(file => IsStartUp(file.Relative)).SelectMany(file => ServingRequests(file.Root)).ToHashSet(StringComparer.Ordinal),
            product
                .Where(file => file.Relative.StartsWith(Controllers, StringComparison.Ordinal))
                .SelectMany(file => Routes(file, constants))
                .ToArray(),
            AreaRoots.SelectMany(areas => Folders(Path.Combine(root, areas))).ToHashSet(StringComparer.Ordinal),
            product
                .SelectMany(file => file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                .SelectMany(declaration => (declaration.BaseList?.Types ?? [])
                    .Select(implemented => Simple(implemented.Type))
                    .Where(domainInterfaces.Contains)
                    .Select(port => KeyValuePair.Create(declaration.Identifier.ValueText, port)))
                .ToLookup(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Projects
                .SelectMany(project => Scanned(root, $"tests/{project}"))
                .Where(file => file.Source.Contains("[Fact", StringComparison.Ordinal) || file.Source.Contains("[Theory", StringComparison.Ordinal))
                .Select(file => Test(Parsed(file)))
                .Where(file => file.Classes.Count > 0)
                .ToArray());
    }

    public static IReadOnlyList<string> FilesInTheLayout(Reading reading)
        => reading.Tests
            .Where(file => file.Seat.Standing == Standing.InTheLayout)
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ClassesOutOfPlace(Reading reading)
        => Offences(reading.Tests, (file, type) => Misplacements(reading, file, type));

    public static IReadOnlyList<string> ClassesUnderAnotherCategory(Reading reading)
        => Offences(InTheLayout(reading), (file, type) => IsUnderItsCategory(file, type)
            ? []
            : [$"is a {file.Seat.Place!.Kind} test but carries {Carried(type)}"]);

    public static IReadOnlyList<string> ClassesServingRequests(Reading reading)
        => reading.ServingRequests.Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> ClassesNamedAfterNothing(
        Reading reading,
        IReadOnlyList<AreaSource> elsewhere,
        IReadOnlyList<AreaSchema> schemas)
        => Offences(InTheLayout(reading), (file, type) => IsNamedAfterItsSubject(reading, file, type, new Listed(elsewhere, schemas))
            ? []
            : [$"is named after {Missing(file.Seat.Place!.Subject, file.Seat.Area)}"]);

    public static IReadOnlyList<string> ActionsWithoutAFeatureClass(Reading reading)
    {
        TestFile[] features = InTheLayout(reading).Where(file => file.Seat.Place!.Kind == Feature).ToArray();
        HashSet<string> areas = features.Select(file => file.Seat.Area).ToHashSet(StringComparer.Ordinal);

        return reading.Types
            .Where(type => type.Key.EndsWith(Action, StringComparison.Ordinal))
            .SelectMany(type => type.Select(relative => new Declared(type.Key, relative)))
            .Where(action => areas.Contains(ControllerArea(action.Relative)))
            .Where(action => !features.Any(file => IsTheFeatureClassOf(file, action)))
            .Select(action => $"{action.Relative} {action.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FeatureClassesSharingASubject(Reading reading)
        => InTheLayout(reading)
            .Where(file => file.Seat.Place!.Kind == Feature)
            .SelectMany(file => file.Classes.Select(type => new Declared(type.Name, file.Relative)))
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(sharing => sharing.Count() > 1)
            .SelectMany(sharing => sharing.Select(type => $"{type.Relative} {type.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> SubjectsBuiltByHand(Reading reading)
        => Offences(
            InTheLayout(reading).Where(file => file.Seat.Place! is
            {
                Kind: DbIntegration,
                Subject: Subject.DomainPort or Subject.AppService or Subject.InfrastructureType,
            }),
            (file, type) => HandBuilding(reading, file, type));

    public static IReadOnlyList<string> ClassesLeftInTheOldPlace(
        Reading reading,
        IReadOnlyCollection<string> notYetMoved,
        IReadOnlyList<AreaSource> elsewhere,
        IReadOnlyList<AreaSchema> schemas)
        => Offences(
            reading.Tests.Where(file => file.Seat.Standing is Standing.InAnOldFolder or Standing.InAFlatOldPlace),
            (file, type) => Ties(reading, file, type, notYetMoved, new Listed(elsewhere, schemas)).Take(1));

    public static IReadOnlyList<string> AreasWronglyListed(
        Reading reading,
        IReadOnlyCollection<string> notYetMoved,
        IReadOnlyList<AreaSource> elsewhere,
        IReadOnlyList<AreaSchema> schemas)
    {
        HashSet<string> inTheLayout = InTheLayout(reading).Select(file => file.Seat.Area).ToHashSet(StringComparer.Ordinal);
        HashSet<string> inTheOldPlace = AreasWithSomethingLeft(reading, new Listed(elsewhere, schemas)).ToHashSet(StringComparer.Ordinal);

        return notYetMoved
            .SelectMany(area => WrongListings(area, reading.Areas, inTheLayout, inTheOldPlace))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> WrongListings(
        string area,
        IReadOnlySet<string> areas,
        IReadOnlySet<string> inTheLayout,
        IReadOnlySet<string> inTheOldPlace)
    {
        if (!areas.Contains(area))
        {
            yield return $"{area} is no area of the product code";

            yield break;
        }

        if (inTheLayout.Contains(area))
        {
            yield return $"{area} already has tests in the layout";
        }

        if (!inTheOldPlace.Contains(area))
        {
            yield return $"{area} has no test left in an old place";
        }
    }

    public static IReadOnlyList<string> SourcesWronglyListed(Reading reading, IReadOnlyList<AreaSource> elsewhere)
        => elsewhere
            .SelectMany(source => WrongSources(reading, source))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> WrongSources(Reading reading, AreaSource source)
    {
        if (!reading.Areas.Contains(source.Area))
        {
            yield return $"{source.Location} is listed for {source.Area}, which is no area of the product code";
        }

        if (!reading.Sources.Any(source.Holds))
        {
            yield return $"{source.Location} is listed for {source.Area} but holds no source file";
        }
    }

    public static IReadOnlyList<string> SchemasWronglyListed(Reading reading, IReadOnlyList<AreaSchema> schemas)
        => schemas
            .SelectMany(schema => WrongSchemas(reading, schema))
            .Concat(schemas
                .GroupBy(schema => schema.Name, StringComparer.Ordinal)
                .Where(listings => listings.Count() > 1)
                .Select(listings => $"{listings.Key} is listed more than once"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> WrongSchemas(Reading reading, AreaSchema schema)
    {
        if (!reading.Areas.Contains(schema.Area))
        {
            yield return $"{schema.Name} is listed for {schema.Area}, which is no area of the product code";
        }

        if (!schema.Name.EndsWith(Schema, StringComparison.Ordinal))
        {
            yield return $"{schema.Name} is listed for {schema.Area} but does not end in {Schema}";
        }

        foreach (string named in reading.Areas.Where(area => string.Equals(schema.Name, area + Schema, StringComparison.Ordinal)))
        {
            yield return string.Equals(named, schema.Area, StringComparison.Ordinal)
                ? $"{schema.Name} is listed for {schema.Area}, which needs no listing for its own name"
                : $"{schema.Name} is listed for {schema.Area} but is named after {named}";
        }

        if (!reading.Tests.Any(file => IsTheSchemaTestOf(file, schema)))
        {
            yield return $"{schema.Name} is listed for {schema.Area} but the database test project has no class {schema.Name}{Suffix}";
        }
    }

    private static bool IsTheSchemaTestOf(TestFile file, AreaSchema schema)
        => string.Equals(file.Project, DbTests, StringComparison.Ordinal)
           && file.Classes.Any(type => string.Equals(type.Name, schema.Name + Suffix, StringComparison.Ordinal));

    private static IEnumerable<string> AreasWithSomethingLeft(Reading reading, Listed listed)
    {
        TestFile[] flat = reading.Tests.Where(file => file.Seat.Standing == Standing.InAFlatOldPlace).ToArray();

        return reading.Tests
            .Where(file => file.Seat.Standing == Standing.InAnOldFolder)
            .Select(file => file.Seat.Area)
            .Concat(flat.SelectMany(file => file.Classes.SelectMany(type => AreasOf(reading, SubjectOf(type), listed))))
            .Concat(flat.SelectMany(ImportedAreas))
            .Concat(reading.Routes
                .Where(route => flat.Any(file => IsAnOldFeatureTest(file) && file.ApiPaths.Any(path => Reaches(path, route.Template))))
                .Select(route => route.Area));
    }

    private static IEnumerable<string> Misplacements(Reading reading, TestFile file, TestClass type)
    {
        if (file.Seat.Standing == Standing.InNoFolderOfTheLayout)
        {
            yield return "sits in no folder of the layout";
        }

        if (file.Seat.Standing != Standing.InTheLayout)
        {
            yield break;
        }

        if (!reading.Areas.Contains(file.Seat.Area))
        {
            yield return $"is under {file.Seat.Area}, which is no area of the product code";
        }

        string space = $"{file.Project}.{string.Join('.', file.Folders)}";

        if (!string.Equals(file.Namespace, space, StringComparison.Ordinal))
        {
            yield return $"is in the namespace {file.Namespace}, not {space}";
        }

        if (!string.Equals(Path.GetFileName(file.Relative), type.Name + ".cs", StringComparison.Ordinal))
        {
            yield return "is not in a file of its own name";
        }
    }

    private static bool IsUnderItsCategory(TestFile file, TestClass type)
        => type.Categories.Count == 1
           && (string.Equals(type.Categories[0], file.Seat.Place!.Kind, StringComparison.Ordinal)
               || CategoriesOfAnyKind.Contains(type.Categories[0], StringComparer.Ordinal));

    private static string Carried(TestClass type)
        => type.Categories.Count == 0 ? "no category" : string.Join(", ", type.Categories);

    private static bool IsNamedAfterItsSubject(Reading reading, TestFile file, TestClass type, Listed listed)
    {
        IReadOnlyList<AreaSource> elsewhere = listed.Sources;
        string subject = SubjectOf(type);
        string area = file.Seat.Area;

        if (subject.Length == 0)
        {
            return false;
        }

        return file.Seat.Place!.Subject switch
        {
            Subject.Endpoint => IsAnActionOf(reading, subject, area) || ServesRequests(reading, subject, area, elsewhere),
            Subject.ApiType => IsDeclaredFor(reading, subject, area, ApiLayer, elsewhere),
            Subject.AppService => subject.EndsWith(Service, StringComparison.Ordinal) && IsDeclaredFor(reading, subject, area, ApiLayer, elsewhere),
            Subject.DomainType => IsDeclaredFor(reading, subject, area, DomainLayer, elsewhere),
            Subject.DomainPort => IsBehindTheDomain(reading, subject) && IsDeclaredFor(reading, PortOf(subject), area, DomainLayer, elsewhere),
            Subject.InfrastructureType => IsDeclaredFor(reading, subject, area, InfrastructureLayer, elsewhere),
            _ => IsOfTheDatabaseProject(reading, subject, area, listed),
        };
    }

    private static string Missing(Subject subject, string area)
        => subject switch
        {
            Subject.Endpoint => $"nothing the API maps for {area}: no action of the area, no class of the area the start-up code puts in the request pipeline or maps an endpoint to",
            Subject.ApiType => $"nothing the API declares for {area}",
            Subject.AppService => $"no service the API declares for {area}",
            Subject.DomainType => $"nothing the domain declares for {area}",
            Subject.DomainPort => $"nothing the domain declares for {area}: nothing behind an interface of its name, no domain service",
            Subject.InfrastructureType => $"nothing the infrastructure declares for {area}",
            _ => $"nothing the database project declares for {area}: no type, no migration, no schema of the area",
        };

    private static bool IsAnActionOf(Reading reading, string subject, string area)
        => subject.EndsWith(Action, StringComparison.Ordinal)
           && reading.Types[subject].Any(relative => relative.StartsWith($"{Controllers}{area}/", StringComparison.Ordinal));

    private static bool IsBehindTheDomain(Reading reading, string subject)
        => subject.EndsWith(Repository, StringComparison.Ordinal)
           || subject.EndsWith(DomainServiceRules.Suffix, StringComparison.Ordinal)
           || reading.DomainPorts[subject].Contains(PortOf(subject), StringComparer.Ordinal)
           || ImplementersInTheInfrastructure(reading, PortOf(subject)).Any();

    /// <summary>The classes the infrastructure declares that implement the domain interface <paramref name="port"/>.</summary>
    private static IEnumerable<string> ImplementersInTheInfrastructure(Reading reading, string port)
        => reading.DomainPorts
            .Where(implemented => implemented.Contains(port, StringComparer.Ordinal)
                                  && reading.Types[implemented.Key].Any(relative => relative.StartsWith(InfrastructureSource, StringComparison.Ordinal)))
            .Select(implemented => implemented.Key);

    private static bool ServesRequests(Reading reading, string subject, string area, IReadOnlyList<AreaSource> elsewhere)
        => reading.ServingRequests.Contains(subject) && IsDeclaredFor(reading, subject, area, ApiLayer, elsewhere);

    private static bool IsOfTheDatabaseProject(Reading reading, string subject, string area, Listed listed)
        => string.Equals(subject, area + Schema, StringComparison.Ordinal)
           || listed.Schemas.Any(schema => string.Equals(schema.Area, area, StringComparison.Ordinal)
                                           && string.Equals(schema.Name, subject, StringComparison.Ordinal))
           || reading.Types[subject]
               .Concat(reading.Migrations[MigrationOf(subject)])
               .Any(relative => relative.StartsWith(DbSource, StringComparison.Ordinal) && IsListedFor(relative, area, listed.Sources));

    private static bool IsDeclaredFor(Reading reading, string name, string area, Layer layer, IReadOnlyList<AreaSource> elsewhere)
        => reading.Types[name].Any(relative => relative.StartsWith($"{layer.Areas}{area}/", StringComparison.Ordinal)
                                               || (relative.StartsWith(layer.Source, StringComparison.Ordinal)
                                                   && IsListedFor(relative, area, elsewhere)));

    private static bool IsListedFor(string relative, string area, IReadOnlyList<AreaSource> elsewhere)
        => elsewhere.Any(source => string.Equals(source.Area, area, StringComparison.Ordinal) && source.Holds(relative));

    private static string MigrationOf(string subject)
        => subject.EndsWith(Migration, StringComparison.Ordinal) ? subject[..^Migration.Length] : string.Empty;

    private static string PortOf(string subject)
        => subject.EndsWith(DomainServiceRules.Suffix, StringComparison.Ordinal) ? subject : "I" + subject;

    private static string SubjectOf(TestClass type)
    {
        string ending = KindOfItsOwn(type) + Suffix;

        return type.Name.EndsWith(ending, StringComparison.Ordinal) ? type.Name[..^ending.Length] : string.Empty;
    }

    private static string KindOfItsOwn(TestClass type)
        => type.Categories.Count == 1 && CategoriesOfAnyKind.Contains(type.Categories[0], StringComparer.Ordinal)
            ? type.Categories[0]
            : string.Empty;

    private static bool IsTheFeatureClassOf(TestFile file, Declared action)
        => string.Equals(file.Seat.Area, ControllerArea(action.Relative), StringComparison.Ordinal)
           && file.Classes.Any(type => string.Equals(type.Name, action.Name + Suffix, StringComparison.Ordinal));

    private static string ControllerArea(string relative)
        => relative.StartsWith(Controllers, StringComparison.Ordinal) && relative[Controllers.Length..].Split('/') is [string area, _, ..]
            ? area
            : string.Empty;

    private static IEnumerable<string> HandBuilding(Reading reading, TestFile file, TestClass type)
    {
        string subject = SubjectOf(type);
        string taken = file.Seat.Place!.Subject == Subject.DomainPort ? PortOf(subject) : subject;

        if (!file.Resolved.Contains(taken)
            && !(file.Seat.Place!.Subject == Subject.InfrastructureType && IsTakenAsInfrastructure(reading, file, subject)))
        {
            yield return $"does not take {taken} from the container";
        }

        foreach (string built in file.Instantiated.Where(built => reading.DomainPorts[built].Any()).Order(StringComparer.Ordinal))
        {
            yield return $"builds {built} with CreateInstance although the container gives it as {string.Join(", ", reading.DomainPorts[built].Order(StringComparer.Ordinal))}";
        }

        if (file.BuildsAnUnnamedType)
        {
            yield return "builds a type it does not name with CreateInstance, which only the helper that wraps a registered service may do";
        }

        if (!string.Equals(taken, subject, StringComparison.Ordinal))
        {
            foreach (string named in ImplementersInTheInfrastructure(reading, taken).Prepend(subject).Distinct(StringComparer.Ordinal).Where(file.Identifiers.Contains).Order(StringComparer.Ordinal))
            {
                yield return $"names the class {named} behind {taken}";
            }
        }

        if (file.Created.Contains(subject))
        {
            yield return $"builds {subject} by hand";
        }
    }

    private static IEnumerable<string> Ties(
        Reading reading,
        TestFile file,
        TestClass type,
        IReadOnlyCollection<string> notYetMoved,
        Listed listed)
    {
        if (file.Seat.Standing == Standing.InAnOldFolder)
        {
            return notYetMoved.Contains(file.Seat.Area)
                ? []
                : [$"is under the old folder {file.Seat.Area}, which is not listed as yet to move"];
        }

        if (notYetMoved.Count == 0)
        {
            return ["is outside the layout, and no area is left to move"];
        }

        return AreasOf(reading, SubjectOf(type), listed)
            .Where(area => !notYetMoved.Contains(area))
            .Order(StringComparer.Ordinal)
            .Select(area => $"is named after a type of {area}, which has moved")
            .Concat(ImportedAreas(file)
                .Where(area => !notYetMoved.Contains(area))
                .Order(StringComparer.Ordinal)
                .Select(area => $"imports the tests of {area}, which has moved"))
            .Concat(SpeaksOnlyToMovedAreas(reading, file, notYetMoved) ? ["speaks only to routes of areas that have moved"] : [])
            .Concat(HasATestSpeakingOnlyToMovedAreas(reading, file, notYetMoved) ? ["has a test that speaks only to routes of areas that have moved"] : [])
            .Concat(HasANamesakeInTheLayout(reading, file, type) ? ["has a namesake in the layout"] : []);
    }

    private static IEnumerable<string> AreasOf(Reading reading, string subject, Listed listed)
        => subject.Length == 0
            ? []
            : reading.Types[subject]
                .Concat(reading.Types["I" + subject])
                .Concat(reading.Migrations[MigrationOf(subject)])
                .Select(relative => AreaOf(relative, listed.Sources))
                .Where(area => area.Length > 0)
                .Concat(reading.Areas.Where(area => string.Equals(subject, area + Schema, StringComparison.Ordinal)))
                .Concat(listed.Schemas.Where(schema => string.Equals(schema.Name, subject, StringComparison.Ordinal)).Select(schema => schema.Area))
                .Distinct(StringComparer.Ordinal);

    private static string AreaOf(string relative, IReadOnlyList<AreaSource> elsewhere)
        => elsewhere.FirstOrDefault(source => source.Holds(relative))?.Area ?? relative.Split('/') switch
        {
            ["", "src", "Carina.Domain" or "Carina.Infrastructure", string area, _, ..] => area,
            ["", "src", "Carina.Api", "Controllers", string area, _, ..] => area,
            _ => string.Empty,
        };

    private static bool IsAHostedServiceFromTheContainer(GenericNameSyntax name)
        => name.Identifier.ValueText == "OfType"
           && name.Parent is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax hosted } } }
           && hosted.Identifier.ValueText == "GetServices"
           && hosted.TypeArgumentList.Arguments is [TypeSyntax service]
           && Simple(service) == "IHostedService";

    private static bool IsCalledWithTheProviderAlone(GenericNameSyntax name)
        => (name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access.Parent : name.Parent)
            is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 };

    private static IEnumerable<string> ImportedAreas(TestFile file)
        => file.Imports
            .Select(space => space.Split('.'))
            .Where(segments => segments.Length > 3)
            .Select(segments => SeatOf(string.Join('.', segments[..3]), segments[3..]))
            .Where(seat => seat.Standing == Standing.InTheLayout)
            .Select(seat => seat.Area)
            .Distinct(StringComparer.Ordinal);

    private static bool SpeaksOnlyToMovedAreas(Reading reading, TestFile file, IReadOnlyCollection<string> notYetMoved)
        => IsAnOldFeatureTest(file)
           && file.ApiPaths.Count > 0
           && file.ApiPaths.All(path => reading.Routes.Any(route => !notYetMoved.Contains(route.Area) && Reaches(path, route.Template)));

    private static bool HasATestSpeakingOnlyToMovedAreas(Reading reading, TestFile file, IReadOnlyCollection<string> notYetMoved)
        => IsAnOldFeatureTest(file)
           && file.PathsOfEachTest.Any(paths => paths.Count > 0
                                                && paths.All(path => RequestedAreas(reading, path) is { Count: > 0 } areas
                                                                     && areas.All(area => !notYetMoved.Contains(area))));

    /// <summary>
    /// The areas whose routes a path a test requests is mapped to: of the templates that take the path, those that
    /// name the most of its segments word for word. A segment the test only partly fills in, such as a name with a
    /// query run onto it, stands for itself rather than for anything.
    /// </summary>
    private static IReadOnlyList<string> RequestedAreas(Reading reading, string path)
    {
        string[] spoken = path.Split('?')[0].Trim('/').Split('/');
        (string Area, int Named)[] taking = reading.Routes
            .Select(route => (route.Area, Named: Named(spoken, Segments(route.Template))))
            .Where(taken => taken.Named >= 0)
            .ToArray();

        return taking.Length == 0
            ? []
            : taking.Where(taken => taken.Named == taking.Max(other => other.Named)).Select(taken => taken.Area).Distinct().ToArray();
    }

    private static int Named(string[] spoken, string[] mapped)
    {
        if (spoken.Length != mapped.Length)
        {
            return -1;
        }

        int named = 0;

        foreach ((string asked, string template) in spoken.Zip(mapped))
        {
            if (string.Equals(asked, template, StringComparison.OrdinalIgnoreCase))
            {
                named++;
            }
            else if (asked != "{}" && template != Hole)
            {
                return -1;
            }
        }

        return named;
    }

    private static bool IsAnOldFeatureTest(TestFile file)
        => string.Equals(file.Project, ApiTests, StringComparison.Ordinal)
           && file.Folders is [OldFeatureFolder, ..];

    private static bool HasANamesakeInTheLayout(Reading reading, TestFile file, TestClass type)
        => InTheLayout(reading).Any(other => string.Equals(other.Project, file.Project, StringComparison.Ordinal)
                                             && other.Classes.Any(named => string.Equals(named.Name, type.Name, StringComparison.Ordinal)));

    private static bool Reaches(string path, string template)
    {
        string[] spoken = Segments(path);
        string[] mapped = Segments(template);

        return spoken.Length == mapped.Length
               && spoken.Zip(mapped).All(pair => pair.First == Hole
                                                 || pair.Second == Hole
                                                 || string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Segments(string path)
        => path.Split('?')[0]
            .Trim('/')
            .Split('/')
            .Select(segment => segment.Contains('{', StringComparison.Ordinal) ? Hole : segment)
            .ToArray();

    private static IEnumerable<TestFile> InTheLayout(Reading reading)
        => reading.Tests.Where(file => file.Seat.Standing == Standing.InTheLayout);

    private static IReadOnlyList<string> Offences(IEnumerable<TestFile> files, Func<TestFile, TestClass, IEnumerable<string>> faults)
        => files
            .SelectMany(file => file.Classes.SelectMany(type => faults(file, type)
                .Select(fault => $"{file.Relative}:{type.Line} {type.Name} {fault}")))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Seat SeatOf(string project, IReadOnlyList<string> folders)
    {
        Place? place = Places.FirstOrDefault(place => place.Holds(project, folders));

        if (place is not null)
        {
            return new Seat(Standing.InTheLayout, place, folders[place.Folders.Count]);
        }

        string top = folders.Count == 0 ? string.Empty : folders[0];

        if (IsAFlatOldPlace(project, folders, top))
        {
            return new Seat(Standing.InAFlatOldPlace, null, string.Empty);
        }

        return KindFolders.Contains(top, StringComparer.Ordinal)
            ? new Seat(Standing.InNoFolderOfTheLayout, null, string.Empty)
            : new Seat(Standing.InAnOldFolder, null, top);
    }

    private static bool IsAFlatOldPlace(string project, IReadOnlyList<string> folders, string top)
    {
        if (top.Length == 0)
        {
            return true;
        }

        return string.Equals(project, ApiTests, StringComparison.Ordinal)
               && (string.Equals(top, OldFeatureFolder, StringComparison.Ordinal) || folders is [Unit]);
    }

    private static TestFile Test(ParsedFile file)
    {
        string[] segments = file.Relative.Split('/');
        string[] folders = segments[3..^1];

        return new TestFile(
            file.Relative,
            segments[2],
            folders,
            SeatOf(segments[2], folders),
            file.Root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty,
            file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(HoldsATest).Select(TestClassOf).ToArray(),
            Identifiers(file.Root).ToHashSet(StringComparer.Ordinal),
            file.Root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Where(directive => directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None) && directive.Name is not null)
                .Select(directive => directive.Name!.ToString())
                .ToHashSet(StringComparer.Ordinal),
            file.Root.DescendantNodes().OfType<GenericNameSyntax>()
                .Where(name => name.TypeArgumentList.Arguments.Count == 1
                               && (name.Identifier.ValueText == "GetRequiredService" || IsAHostedServiceFromTheContainer(name)))
                .Select(name => Simple(name.TypeArgumentList.Arguments[0]))
                .ToHashSet(StringComparer.Ordinal),
            file.Root.DescendantNodes().OfType<GenericNameSyntax>()
                .Where(name => name.Identifier.ValueText == "CreateInstance"
                               && name.TypeArgumentList.Arguments.Count == 1
                               && IsCalledWithTheProviderAlone(name))
                .Select(name => Simple(name.TypeArgumentList.Arguments[0]))
                .ToHashSet(StringComparer.Ordinal),
            Generic(file.Root, "CreateInstance"),
            Generic(file.Root, "IsType"),
            file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsAnUnnamedCreateInstance),
            file.Root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Select(Created).ToHashSet(StringComparer.Ordinal),
            ApiPaths(file.Root),
            file.Root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => Attributes(method.AttributeLists).Any(attribute => Simple(attribute.Name) is "Fact" or "Theory"))
                .Select(method => (IReadOnlyList<string>)RequestedPaths(method))
                .ToArray());
    }

    /// <summary>
    /// The paths a test hands to a request of its own: an argument of a call that sends one, or of a <c>Uri</c> it
    /// builds, so that a path read out of a served document or a table of routes is not taken for a request.
    /// </summary>
    private static string[] RequestedPaths(SyntaxNode method)
        => method.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Where(argument => argument.Parent?.Parent switch
            {
                InvocationExpressionSyntax call => RequestCalls.Contains(Simple(call.Expression is MemberAccessExpressionSyntax access ? access.Name : call.Expression)),
                ObjectCreationExpressionSyntax created => Simple(created.Type) == "Uri",
                _ => false,
            })
            .Select(argument => Spoken(argument.Expression))
            .Where(path => path.StartsWith("/api/", StringComparison.Ordinal))
            .ToArray();

    private static string[] ApiPaths(SyntaxNode node)
        => node.DescendantNodes().Select(Spoken).Where(path => path.StartsWith("/api/", StringComparison.Ordinal)).ToArray();

    private static bool IsAnUnnamedCreateInstance(InvocationExpressionSyntax call)
        => (call.Expression is MemberAccessExpressionSyntax access ? access.Name : call.Expression)
            is IdentifierNameSyntax { Identifier.ValueText: "CreateInstance" };

    private static HashSet<string> Generic(SyntaxNode root, string method)
        => root.DescendantNodes().OfType<GenericNameSyntax>()
            .Where(name => name.Identifier.ValueText == method && name.TypeArgumentList.Arguments.Count == 1)
            .Select(name => Simple(name.TypeArgumentList.Arguments[0]))
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsTakenAsInfrastructure(Reading reading, TestFile file, string subject)
        => reading.DomainPorts[subject].Any()
            ? file.Narrowed.Contains(subject) && reading.DomainPorts[subject].Any(file.Resolved.Contains)
            : file.Composed.Contains(subject);

    private static bool HoldsATest(ClassDeclarationSyntax type)
        => type.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(method => Attributes(method.AttributeLists).Any(attribute => Simple(attribute.Name) is "Fact" or "Theory"));

    private static TestClass TestClassOf(ClassDeclarationSyntax type)
        => new(
            type.Identifier.ValueText,
            type.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Attributes(type.AttributeLists)
                .Where(attribute => Simple(attribute.Name) == "Trait")
                .Select(attribute => attribute.ArgumentList?.Arguments.Select(argument => Literal(argument.Expression)).ToArray() ?? [])
                .Where(arguments => arguments is ["Category", _])
                .Select(arguments => arguments[1])
                .ToArray());

    private static IEnumerable<AttributeSyntax> Attributes(SyntaxList<AttributeListSyntax> lists)
        => lists.SelectMany(list => list.Attributes);

    private static string Created(BaseObjectCreationExpressionSyntax creation)
        => creation switch
        {
            ObjectCreationExpressionSyntax named => Simple(named.Type),
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } } => Simple(declaration.Type),
            _ => string.Empty,
        };

    private static string Simple(SyntaxNode name)
        => name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            NullableTypeSyntax nullable => Simple(nullable.ElementType),
            _ => string.Empty,
        };

    private static string Spoken(SyntaxNode node)
        => node switch
        {
            InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(
                content => content is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : "{}")),
            _ => Literal(node),
        };

    private static string Literal(SyntaxNode? node)
        => node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : string.Empty;

    private static IEnumerable<string> Identifiers(SyntaxNode root)
        => root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText);

    private static IEnumerable<KeyValuePair<string, string>> Constants(FieldDeclarationSyntax field)
        => field.Declaration.Variables
            .Where(variable => Literal(variable.Initializer?.Value).Length > 0)
            .Select(variable => KeyValuePair.Create(
                $"{field.Ancestors().OfType<BaseTypeDeclarationSyntax>().First().Identifier.ValueText}.{variable.Identifier.ValueText}",
                Literal(variable.Initializer!.Value)));

    private static IEnumerable<Route> Routes(ParsedFile file, ILookup<string, string> constants)
        => file.Root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .Where(attribute => Simple(attribute.Name) == "Route" && attribute.ArgumentList?.Arguments.Count == 1)
            .Select(attribute => attribute.ArgumentList!.Arguments[0].Expression)
            .Select(template => template is MemberAccessExpressionSyntax constant
                ? constants[$"{Simple(constant.Expression)}.{constant.Name.Identifier.ValueText}"].FirstOrDefault(string.Empty)
                : Literal(template))
            .Where(template => template.Length > 0)
            .Select(template => new Route(ControllerArea(file.Relative), template));

    private static IEnumerable<string> ServingRequests(SyntaxNode root)
        => root.DescendantNodes().OfType<InvocationExpressionSyntax>().SelectMany(ServingRequests).Where(name => name.Length > 0);

    private static IEnumerable<string> ServingRequests(InvocationExpressionSyntax call)
    {
        SimpleNameSyntax? method = call.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            SimpleNameSyntax simple => simple,
            _ => null,
        };
        string name = method?.Identifier.ValueText ?? string.Empty;
        IEnumerable<string> generic = method is GenericNameSyntax named ? named.TypeArgumentList.Arguments.Select(Simple) : [];
        IEnumerable<ExpressionSyntax> arguments = call.ArgumentList.Arguments.Select(argument => argument.Expression);

        if (name.StartsWith(MiddlewareCall, StringComparison.Ordinal) || IsAFilterCall(call, name))
        {
            return generic.Concat(arguments.Select(Named));
        }

        if (name.StartsWith(EndpointCall, StringComparison.Ordinal))
        {
            return generic.Concat(arguments.OfType<LambdaExpressionSyntax>().Select(Handler));
        }

        return string.Equals(name, SchemeCall, StringComparison.Ordinal) ? generic.TakeLast(1) : [];
    }

    private static bool IsAFilterCall(InvocationExpressionSyntax call, string name)
        => string.Equals(name, EndpointFilterCall, StringComparison.Ordinal)
           || (name.StartsWith(FilterCall, StringComparison.Ordinal)
               && call.Expression is MemberAccessExpressionSyntax { Expression: ExpressionSyntax owner }
               && string.Equals(Simple(owner is MemberAccessExpressionSyntax access ? access.Name : owner), Filters, StringComparison.Ordinal));

    private static string Named(ExpressionSyntax argument)
        => argument switch
        {
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } => owner.Identifier.ValueText,
            TypeOfExpressionSyntax asked => Simple(asked.Type),
            ObjectCreationExpressionSyntax created => Simple(created.Type),
            _ => string.Empty,
        };

    private static string Handler(LambdaExpressionSyntax lambda)
    {
        IEnumerable<ParameterSyntax> parameters = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax several => several.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax one => [one.Parameter],
            _ => [],
        };

        return lambda.ExpressionBody is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } }
               && !parameters.Any(parameter => string.Equals(parameter.Identifier.ValueText, owner.Identifier.ValueText, StringComparison.Ordinal))
            ? owner.Identifier.ValueText
            : string.Empty;
    }

    private static bool IsStartUp(string relative)
        => string.Equals(relative, ApiSource + "Program.cs", StringComparison.Ordinal)
           || relative.StartsWith(ApiSource + "Extensions/", StringComparison.Ordinal);

    private static ILookup<string, string> Migrations(string root)
    {
        string directory = Path.Combine(root, "src", "Carina.Db", "Migrations");
        IEnumerable<string> files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.cs") : [];

        return files
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Where(name => name.Split('_') is [string stamp, string migration]
                           && stamp.All(char.IsAsciiDigit)
                           && !migration.Contains('.', StringComparison.Ordinal))
            .ToLookup(name => name.Split('_')[1], name => $"{DbSource}Migrations/{name}.cs", StringComparer.Ordinal);
    }

    private static IEnumerable<string> Folders(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory).Select(folder => Path.GetFileName(folder)!).Where(folder => folder is not ("bin" or "obj"))
            : [];

    private static ParsedFile Parsed(SourceFile file)
        => new(file.Relative, CSharpSyntaxTree.ParseText(file.Source).GetRoot());

    private static IEnumerable<SourceFile> Scanned(string root, string folder)
    {
        string directory = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));

        return Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutputOrGenerated(Path.GetRelativePath(root, file)))
                .Select(file => new SourceFile("/" + Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file)))
            : [];
    }

    private static bool IsBuildOutputOrGenerated(string relative)
    {
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal)
               || segments.Contains("Migrations", StringComparer.Ordinal)
               || relative.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    /// <summary>What <see cref="Read"/> found under one root: the product code's names and the test classes.</summary>
    public sealed class Reading
    {
        internal Reading(
            IReadOnlySet<string> sources,
            ILookup<string, string> types,
            ILookup<string, string> migrations,
            IReadOnlySet<string> servingRequests,
            IReadOnlyList<Route> routes,
            IReadOnlySet<string> areas,
            ILookup<string, string> domainPorts,
            IReadOnlyList<TestFile> tests)
        {
            Sources = sources;
            Types = types;
            Migrations = migrations;
            ServingRequests = servingRequests;
            Routes = routes;
            Areas = areas;
            DomainPorts = domainPorts;
            Tests = tests;
        }

        internal IReadOnlySet<string> Sources { get; }

        internal ILookup<string, string> Types { get; }

        internal ILookup<string, string> Migrations { get; }

        internal IReadOnlySet<string> ServingRequests { get; }

        internal IReadOnlyList<Route> Routes { get; }

        internal IReadOnlySet<string> Areas { get; }

        /// <summary>For each class of the product, the interfaces declared under the domain that it implements.</summary>
        internal ILookup<string, string> DomainPorts { get; }

        internal IReadOnlyList<TestFile> Tests { get; }
    }

    /// <summary>A folder or a file that belongs to an area although it is not under the folder of the area's name.</summary>
    public sealed record AreaSource(string Area, string Location)
    {
        internal bool Holds(string relative)
            => Location.EndsWith('/')
                ? relative.StartsWith("/" + Location, StringComparison.Ordinal)
                : string.Equals(relative, "/" + Location, StringComparison.Ordinal);
    }

    /// <summary>A schema test of an area whose name is not the area's own: <c>Name</c> is the class name without <c>Tests</c>.</summary>
    public sealed record AreaSchema(string Area, string Name);

    internal sealed record TestFile(
        string Relative,
        string Project,
        IReadOnlyList<string> Folders,
        Seat Seat,
        string Namespace,
        IReadOnlyList<TestClass> Classes,
        IReadOnlySet<string> Identifiers,
        IReadOnlySet<string> Imports,
        IReadOnlySet<string> Resolved,
        IReadOnlySet<string> Composed,
        IReadOnlySet<string> Instantiated,
        IReadOnlySet<string> Narrowed,
        bool BuildsAnUnnamedType,
        IReadOnlySet<string> Created,
        IReadOnlyList<string> ApiPaths,
        IReadOnlyList<IReadOnlyList<string>> PathsOfEachTest);

    internal sealed record TestClass(string Name, int Line, IReadOnlyList<string> Categories);

    internal sealed record Seat(Standing Standing, Place? Place, string Area);

    internal sealed record Place(string Project, IReadOnlyList<string> Folders, string Kind, Subject Subject)
    {
        public bool Holds(string project, IReadOnlyList<string> folders)
            => string.Equals(project, Project, StringComparison.Ordinal)
               && folders.Count > Folders.Count
               && folders.Take(Folders.Count).SequenceEqual(Folders, StringComparer.Ordinal);
    }

    internal readonly record struct Route(string Area, string Template);

    private readonly record struct Listed(IReadOnlyList<AreaSource> Sources, IReadOnlyList<AreaSchema> Schemas);

    private readonly record struct Layer(string Source, string Areas);

    private readonly record struct Declared(string Name, string Relative);

    private readonly record struct SourceFile(string Relative, string Source);

    private readonly record struct ParsedFile(string Relative, SyntaxNode Root);
}
