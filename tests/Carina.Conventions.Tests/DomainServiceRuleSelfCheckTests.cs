using Carina.Conventions.Tests.Fixtures.DomainServices.Domain;
using Carina.Conventions.Tests.Fixtures.DomainServices.Outer;

namespace Carina.Conventions.Tests;

public sealed class DomainServiceRuleSelfCheckTests
{
    private const string FixtureDomain = "Carina.Conventions.Tests.Fixtures.DomainServices.Domain";

    private static readonly IReadOnlyList<Type> Fixtures =
    [
        typeof(IShelf),
        typeof(ILedgerDomainService),
        typeof(ShelfDomainService),
        typeof(ShelfReader),
        typeof(Clocked),
        typeof(ShelfFound),
        typeof(ShelfKeeper),
        typeof(ShelfHeld),
        typeof(Tally),
        typeof(UnsealedDomainService),
        typeof(StaticDomainService),
        typeof(NamedDomainService),
        typeof(ListedDomainService),
        typeof(StrayDomainService),
    ];

    [Fact]
    public void DetectsADomainClassThatTakesARepositoryOrTheClockUnderAnotherName()
    {
        Assert.Equal(
            [typeof(Clocked).FullName!, typeof(ShelfReader).FullName!],
            DomainServiceRules.DomainClassesTakingAPortUnderAnotherName(
                [typeof(Clocked), typeof(ShelfReader), typeof(Tally), typeof(ShelfDomainService)],
                FixtureDomain,
                []));
    }

    [Fact]
    public void DetectsAClassThatTakesARepositoryAndHandsItBackOutThroughAProperty()
    {
        Assert.Equal(
            [typeof(ShelfFound).FullName!, typeof(ShelfHeld).FullName!, typeof(ShelfKeeper).FullName!],
            DomainServiceRules.DomainClassesTakingAPortUnderAnotherName(
                [typeof(ShelfFound), typeof(ShelfKeeper), typeof(ShelfHeld)],
                FixtureDomain,
                []));
    }

    [Fact]
    public void LetsOffOnlyTheOutcomesItIsHanded()
    {
        Assert.Equal(
            [typeof(Clocked).FullName!, typeof(ShelfHeld).FullName!, typeof(ShelfKeeper).FullName!, typeof(ShelfReader).FullName!],
            DomainServiceRules.DomainClassesTakingAPortUnderAnotherName(Fixtures, FixtureDomain, [typeof(ShelfFound)]));
    }

    [Fact]
    public void DetectsAListedOutcomeThatNoLongerTakesAHandle()
    {
        Assert.Equal(
            [typeof(Tally).FullName!],
            DomainServiceRules.ListedOutcomesTakingNoPort(Fixtures, FixtureDomain, [typeof(ShelfFound), typeof(Tally)]));
    }

    [Fact]
    public void DetectsADomainServiceOutsideTheDomain()
    {
        Assert.Equal(
            [typeof(StrayDomainService).FullName!],
            DomainServiceRules.DomainServicesOutsideTheDomain(Fixtures, FixtureDomain));
    }

    [Fact]
    public void DetectsADomainServiceOfAnotherShape()
    {
        Assert.Equal(
            [
                $"{typeof(ILedgerDomainService).FullName} is not a class",
                $"{typeof(ListedDomainService).FullName} takes IReadOnlyList`1",
                $"{typeof(NamedDomainService).FullName} takes String",
                $"{typeof(StaticDomainService).FullName} is static",
                $"{typeof(UnsealedDomainService).FullName} is not sealed",
            ],
            DomainServiceRules.MisshapenDomainServices(Fixtures, FixtureDomain));
    }

    [Fact]
    public void ListsTheDomainServicesItFinds()
    {
        Assert.Contains(typeof(ShelfDomainService).FullName!, DomainServiceRules.DomainServices(Fixtures, FixtureDomain));
        Assert.DoesNotContain(typeof(StrayDomainService).FullName!, DomainServiceRules.DomainServices(Fixtures, FixtureDomain));
    }

    [Fact]
    public void DetectsADomainServiceDeclaredInAFileOfAnotherName()
    {
        using SourceTree tree = new();
        tree.Write("Carina.Domain/Shelves/ShelfDomainService.cs", "namespace Carina.Domain.Shelves;\n\npublic sealed class ShelfDomainService { }");
        tree.Write("Carina.Domain/Shelves/Shelves.cs", "namespace Carina.Domain.Shelves;\n\npublic sealed class CountDomainService { }");

        Assert.Equal(
            ["/Carina.Domain/Shelves/Shelves.cs:3 CountDomainService"],
            DomainServiceRules.DomainServicesOutOfPlace(tree.Root));
    }

    [Fact]
    public void DetectsADomainServiceDeclaredOutsideTheDomain()
    {
        using SourceTree tree = new();
        tree.Write("Carina.Infrastructure/Shelves/ShelfDomainService.cs", "namespace Carina.Infrastructure.Shelves;\n\npublic sealed class ShelfDomainService { }");
        tree.Write("Carina.Api/Services/ShelfService.cs", "namespace Carina.Api.Services;\n\npublic sealed class ShelfService { private sealed record SpareDomainService; }");

        Assert.Equal(
            [
                "/Carina.Api/Services/ShelfService.cs:3 SpareDomainService",
                "/Carina.Infrastructure/Shelves/ShelfDomainService.cs:3 ShelfDomainService",
            ],
            DomainServiceRules.DomainServicesOutOfPlace(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-domain-service-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
