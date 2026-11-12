using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// The tests of the API, the domain, the infrastructure and the database project are laid out by kind and
/// by the layer of what they test: <c>Feature/&lt;Area&gt;</c>, <c>Unit/App/&lt;Area&gt;</c> and
/// <c>DbIntegration/App/&lt;Area&gt;</c> in the API's tests, <c>Unit/&lt;Area&gt;</c> in the domain's,
/// <c>DbIntegration/Domain/&lt;Area&gt;</c>, <c>DbIntegration/Infra/&lt;Area&gt;</c> and
/// <c>Unit/Infra/&lt;Area&gt;</c> in the infrastructure's, and the last two in the database project's. An
/// area is a folder directly under the domain, the infrastructure or the controllers. A class in the layout
/// stands alone in a file of its own name, in the namespace of its folder, carries one <c>Category</c>
/// trait naming its kind (or <c>Scale</c> or <c>Material</c>), and is named after what it tests: an action
/// of its area or a class the start-up code names for a feature test, a type of the same layer otherwise,
/// a repository behind an interface of the domain or a domain service under <c>DbIntegration/Domain</c>,
/// and a migration or a schema in the database project. Every action of an area that has feature tests has
/// its class. Under <c>DbIntegration/Domain</c> and <c>DbIntegration/App</c> the subject comes out of the
/// container.
/// <para>
/// A test class outside the layout is in an old place. <see cref="AreasNotYetMoved"/> names the areas whose
/// tests may still be there. A folder of an old place must be listed. In the flat old places (a project's
/// root, <c>FeatureTest</c>, the files directly under the API's <c>Unit</c>) a class is tied to an area by
/// the type it is named after, by speaking only to routes of that area's actions, or by a namesake in the
/// layout; what these ties miss is caught when the list is empty, for then every class outside the layout
/// is reported. The rules read the syntax tree and no build output.
/// </para>
/// </summary>
public static class TestLayoutRules
{
    public const string Feature = "Feature";

    public const string Unit = "Unit";

    public const string DbIntegration = "DbIntegration";

    public static readonly IReadOnlyList<string> AreasNotYetMoved =
    [
        "Base",
        "Captions",
        "Channels",
        "Collection",
        "Configuration",
        "DataBroadcast",
        "DependencyInjection",
        "Driver",
        "DriverStatus",
        "Encoding",
        "Encodings",
        "Epg",
        "Events",
        "Health",
        "Integrity",
        "Library",
        "Live",
        "Logos",
        "Machines",
        "Migration",
        "Net",
        "Notifications",
        "Persistence",
        "Playback",
        "Programmes",
        "Quality",
        "Recordings",
        "Reservations",
        "Rules",
        "Scanning",
        "Scans",
        "Segments",
        "Services",
        "Storage",
        "Streaming",
        "Thumbnails",
        "Tuners",
        "Version",
        "Videos",
        "Viewing",
    ];

    public static readonly IReadOnlyDictionary<string, string> ApiFoldersOfAnArea =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Authentication"] = "Auth" };

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

    private const string Hole = "*";

    private static readonly string[] Projects = [ApiTests, DomainTests, InfrastructureTests, DbTests];

    private static readonly string[] KindFolders = [Feature, Unit, DbIntegration];

    private static readonly string[] CategoriesOfAnyKind = ["Scale", "Material"];

    private static readonly string[] AreaRoots = ["src/Carina.Domain", "src/Carina.Infrastructure", "src/Carina.Api/Controllers"];

    private static readonly IReadOnlyList<Place> Places =
    [
        new(ApiTests, [Feature], Feature, Subject.Endpoint),
        new(ApiTests, [Unit, "App"], Unit, Subject.ApiType),
        new(ApiTests, [DbIntegration, "App"], DbIntegration, Subject.ApiType),
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

        return new Reading(
            product
                .SelectMany(file => file.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                    .Select(declaration => new Declared(declaration.Identifier.ValueText, file.Relative)))
                .ToLookup(declared => declared.Name, declared => declared.Relative, StringComparer.Ordinal),
            Migrations(root),
            product.Where(file => IsStartUp(file.Relative)).SelectMany(file => Identifiers(file.Root)).ToHashSet(StringComparer.Ordinal),
            product
                .Where(file => file.Relative.StartsWith(Controllers, StringComparison.Ordinal))
                .SelectMany(file => Routes(file, constants))
                .ToArray(),
            AreaRoots.SelectMany(areas => Folders(Path.Combine(root, areas))).ToHashSet(StringComparer.Ordinal),
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

    public static IReadOnlyList<string> ClassesNamedAfterNothing(Reading reading)
        => Offences(InTheLayout(reading), (file, type) => IsNamedAfterItsSubject(reading, file, type)
            ? []
            : [$"is named after nothing {file.Seat.Place!.Subject switch
            {
                Subject.Endpoint => $"the API maps: no action of {file.Seat.Area}, no class the start-up code names",
                Subject.ApiType => "the API declares",
                Subject.DomainType => "the domain declares",
                Subject.DomainPort => "the domain declares: no repository behind its interface, no domain service",
                Subject.InfrastructureType => "the infrastructure declares",
                _ => "the database project declares: no type, no migration, no schema",
            }}"]);

    public static IReadOnlyList<string> ActionsWithoutAFeatureClass(Reading reading)
    {
        TestFile[] features = InTheLayout(reading).Where(file => file.Seat.Place!.Kind == Feature).ToArray();
        HashSet<string> areas = features.Select(file => file.Seat.Area).ToHashSet(StringComparer.Ordinal);

        return reading.Types
            .Where(type => type.Key.EndsWith("Action", StringComparison.Ordinal))
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
            InTheLayout(reading).Where(file => file.Seat.Place! is { Kind: DbIntegration, Subject: Subject.DomainPort or Subject.ApiType }),
            HandBuilding);

    public static IReadOnlyList<string> ClassesLeftInTheOldPlace(
        Reading reading,
        IReadOnlyCollection<string> notYetMoved,
        IReadOnlyDictionary<string, string> apiFolders)
        => Offences(
            reading.Tests.Where(file => file.Seat.Standing is Standing.InAnOldFolder or Standing.InAFlatOldPlace),
            (file, type) => Ties(reading, file, type, notYetMoved, apiFolders).Take(1));

    public static IReadOnlyList<string> AreasWronglyListed(
        Reading reading,
        IReadOnlyCollection<string> notYetMoved,
        IReadOnlyDictionary<string, string> apiFolders)
    {
        HashSet<string> inTheLayout = InTheLayout(reading).Select(file => file.Seat.Area).ToHashSet(StringComparer.Ordinal);
        HashSet<string> inTheOldPlace = AreasWithSomethingLeft(reading, apiFolders).ToHashSet(StringComparer.Ordinal);

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

    private static IEnumerable<string> AreasWithSomethingLeft(Reading reading, IReadOnlyDictionary<string, string> apiFolders)
    {
        TestFile[] flat = reading.Tests.Where(file => file.Seat.Standing == Standing.InAFlatOldPlace).ToArray();

        return reading.Tests
            .Where(file => file.Seat.Standing == Standing.InAnOldFolder)
            .Select(file => file.Seat.Area)
            .Concat(flat.SelectMany(file => file.Classes.SelectMany(type => AreasOf(reading, SubjectOf(type.Name), apiFolders))))
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

    private static bool IsNamedAfterItsSubject(Reading reading, TestFile file, TestClass type)
    {
        string subject = SubjectOf(type.Name);

        if (subject.Length == 0)
        {
            return false;
        }

        return file.Seat.Place!.Subject switch
        {
            Subject.Endpoint => IsDeclaredUnder(reading, subject, $"{Controllers}{file.Seat.Area}/") || IsNamedByTheStartUp(reading, subject),
            Subject.ApiType => IsDeclaredUnder(reading, subject, ApiSource),
            Subject.DomainType => IsDeclaredUnder(reading, subject, DomainSource),
            Subject.DomainPort => IsDeclaredUnder(reading, PortOf(subject), DomainSource),
            Subject.InfrastructureType => IsDeclaredUnder(reading, subject, InfrastructureSource),
            _ => IsDeclaredUnder(reading, subject, DbSource) || IsAMigrationOrASchema(reading, subject),
        };
    }

    private static bool IsNamedByTheStartUp(Reading reading, string subject)
        => reading.NamedByTheStartUp.Contains(subject)
           && reading.Types[subject].Any(relative => relative.StartsWith(ApiSource, StringComparison.Ordinal)
                                                     && !relative.StartsWith(Controllers, StringComparison.Ordinal));

    private static bool IsAMigrationOrASchema(Reading reading, string subject)
        => subject.EndsWith("Schema", StringComparison.Ordinal)
           || (subject.EndsWith("Migration", StringComparison.Ordinal) && reading.Migrations.Contains(subject[..^"Migration".Length]));

    private static bool IsDeclaredUnder(Reading reading, string name, string folder)
        => reading.Types[name].Any(relative => relative.StartsWith(folder, StringComparison.Ordinal));

    private static string PortOf(string subject)
        => subject.EndsWith(DomainServiceRules.Suffix, StringComparison.Ordinal) ? subject : "I" + subject;

    private static string SubjectOf(string name)
        => name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : string.Empty;

    private static bool IsTheFeatureClassOf(TestFile file, Declared action)
        => string.Equals(file.Seat.Area, ControllerArea(action.Relative), StringComparison.Ordinal)
           && file.Classes.Any(type => string.Equals(type.Name, action.Name + Suffix, StringComparison.Ordinal));

    private static string ControllerArea(string relative)
        => relative.StartsWith(Controllers, StringComparison.Ordinal) && relative[Controllers.Length..].Split('/') is [string area, _, ..]
            ? area
            : string.Empty;

    private static IEnumerable<string> HandBuilding(TestFile file, TestClass type)
    {
        string subject = SubjectOf(type.Name);
        string taken = file.Seat.Place!.Subject == Subject.DomainPort ? PortOf(subject) : subject;

        if (!file.Resolved.Contains(taken))
        {
            yield return $"does not take {taken} from the container";
        }

        if (!string.Equals(taken, subject, StringComparison.Ordinal) && file.Identifiers.Contains(subject))
        {
            yield return $"names the class {subject} behind {taken}";
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
        IReadOnlyDictionary<string, string> apiFolders)
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

        return AreasOf(reading, SubjectOf(type.Name), apiFolders)
            .Where(area => !notYetMoved.Contains(area))
            .Order(StringComparer.Ordinal)
            .Select(area => $"is named after a type of {area}, which has moved")
            .Concat(SpeaksOnlyToMovedAreas(reading, file, notYetMoved) ? ["speaks only to routes of areas that have moved"] : [])
            .Concat(HasANamesakeInTheLayout(reading, file, type) ? ["has a namesake in the layout"] : []);
    }

    private static IEnumerable<string> AreasOf(Reading reading, string subject, IReadOnlyDictionary<string, string> apiFolders)
        => subject.Length == 0
            ? []
            : reading.Types[subject]
                .Concat(reading.Types["I" + subject])
                .Select(relative => AreaOf(relative, apiFolders))
                .Where(area => area.Length > 0)
                .Concat(reading.Areas.Where(area => string.Equals(subject, area + "Schema", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal);

    private static string AreaOf(string relative, IReadOnlyDictionary<string, string> apiFolders)
        => relative.Split('/') switch
        {
            ["", "src", "Carina.Domain" or "Carina.Infrastructure", string area, _, ..] => area,
            ["", "src", "Carina.Api", "Controllers", string area, _, ..] => area,
            ["", "src", "Carina.Api", string folder, _, ..] => apiFolders.GetValueOrDefault(folder, string.Empty),
            _ => string.Empty,
        };

    private static bool SpeaksOnlyToMovedAreas(Reading reading, TestFile file, IReadOnlyCollection<string> notYetMoved)
        => IsAnOldFeatureTest(file)
           && file.ApiPaths.Count > 0
           && file.ApiPaths.All(path => reading.Routes.Any(route => !notYetMoved.Contains(route.Area) && Reaches(path, route.Template)));

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
            file.Root.DescendantNodes().OfType<GenericNameSyntax>()
                .Where(name => name.Identifier.ValueText == "GetRequiredService" && name.TypeArgumentList.Arguments.Count == 1)
                .Select(name => Simple(name.TypeArgumentList.Arguments[0]))
                .ToHashSet(StringComparer.Ordinal),
            file.Root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Select(Created).ToHashSet(StringComparer.Ordinal),
            file.Root.DescendantNodes().Select(Spoken).Where(path => path.StartsWith("/api/", StringComparison.Ordinal)).ToArray());
    }

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

    private static bool IsStartUp(string relative)
        => string.Equals(relative, ApiSource + "Program.cs", StringComparison.Ordinal)
           || relative.StartsWith(ApiSource + "Extensions/", StringComparison.Ordinal);

    private static HashSet<string> Migrations(string root)
    {
        string directory = Path.Combine(root, "src", "Carina.Db", "Migrations");

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.cs")
            .Select(file => Path.GetFileNameWithoutExtension(file).Split('_'))
            .Where(parts => parts is [_, _] && parts[0].All(char.IsAsciiDigit) && !parts[1].Contains('.', StringComparison.Ordinal))
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.Ordinal);
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
            ILookup<string, string> types,
            IReadOnlySet<string> migrations,
            IReadOnlySet<string> namedByTheStartUp,
            IReadOnlyList<Route> routes,
            IReadOnlySet<string> areas,
            IReadOnlyList<TestFile> tests)
        {
            Types = types;
            Migrations = migrations;
            NamedByTheStartUp = namedByTheStartUp;
            Routes = routes;
            Areas = areas;
            Tests = tests;
        }

        internal ILookup<string, string> Types { get; }

        internal IReadOnlySet<string> Migrations { get; }

        internal IReadOnlySet<string> NamedByTheStartUp { get; }

        internal IReadOnlyList<Route> Routes { get; }

        internal IReadOnlySet<string> Areas { get; }

        internal IReadOnlyList<TestFile> Tests { get; }
    }

    internal sealed record TestFile(
        string Relative,
        string Project,
        IReadOnlyList<string> Folders,
        Seat Seat,
        string Namespace,
        IReadOnlyList<TestClass> Classes,
        IReadOnlySet<string> Identifiers,
        IReadOnlySet<string> Resolved,
        IReadOnlySet<string> Created,
        IReadOnlyList<string> ApiPaths);

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

    private readonly record struct Declared(string Name, string Relative);

    private readonly record struct SourceFile(string Relative, string Source);

    private readonly record struct ParsedFile(string Relative, SyntaxNode Root);
}
