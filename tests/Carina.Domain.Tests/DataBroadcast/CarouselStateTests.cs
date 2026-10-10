using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class CarouselStateTests
{
    [Fact(DisplayName = "BR-BD-004: a service found carrying a data broadcast gives the catalog of where it is entered")]
    public void ACarriedServiceGivesTheCatalogOfItsEntry()
    {
        CarouselState state = new();

        IReadOnlyList<CarouselDelta> deltas = state.Apply(Carousels.Carried(autoStart: true), 0);

        CarouselCatalog catalog = Assert.IsType<CarouselDelta.CatalogChanged>(Assert.Single(deltas)).Catalog;
        Assert.Equal(1024, catalog.Service.Value);
        Assert.Equal(0x40, catalog.EntryTag);
        Assert.True(catalog.AutoStart);
        Assert.Equal("/40/0000/startup.bml", catalog.StartupDocument);
        Assert.Empty(catalog.Carousels);
    }

    [Fact]
    public void TheSameEntryReadAgainChangesNothing()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);

        Assert.Empty(state.Apply(Carousels.Carried(), 10));
    }

    [Fact(DisplayName = "BR-BD-004: a service with no data broadcast says so once")]
    public void AServiceWithNoDataBroadcastSaysSoOnce()
    {
        CarouselState state = new();

        Assert.IsType<CarouselDelta.Absent>(Assert.Single(state.Apply(new CarouselSignal.NotCarried(), 0)));
        Assert.Empty(state.Apply(new CarouselSignal.NotCarried(), 10));
        Assert.True(state.IsAbsent);
        Assert.Null(state.Catalog);
    }

    [Fact]
    public void AServiceThatStopsCarryingADataBroadcastForgetsEveryModule()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        state.Apply(new CarouselSignal.NotCarried(), 20);

        Assert.Empty(state.Modules);
        Assert.Empty(state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 30));
        Assert.Empty(state.Apply(new CarouselSignal.EventTimed(Carousels.Event(1, 40)), 40));
    }

    [Fact(DisplayName = "BR-BS-002: the catalog lists the modules of the download info, none of them arrived yet")]
    public void TheCatalogListsWhatTheDownloadInfoListsBeforeAnyModuleArrives()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);

        IReadOnlyList<CarouselDelta> deltas = state.Apply(Carousels.Listing(Carousels.Entry, (0, 1), (1, 3)), 10);

        CatalogCarousel carousel = Assert.Single(Assert.IsType<CarouselDelta.CatalogChanged>(Assert.Single(deltas)).Catalog.Carousels);
        Assert.Equal(Carousels.Entry, carousel.Tag);
        Assert.Equal([(0, 1, false), (1, 3, false)], carousel.Modules.Select(module => (module.Id, module.Version, module.Arrived)));
        Assert.All(carousel.Modules, module => Assert.Empty(module.Resources));
    }

    [Fact(DisplayName = "BR-BD-004: a module that arrives comes before the catalog that says it has arrived")]
    public void AModuleThatArrivesComesBeforeTheCatalogThatSaysSo()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);

        IReadOnlyList<CarouselDelta> deltas = state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 90_000);

        Assert.Equal(2, deltas.Count);
        ModuleVersion arrived = Assert.IsType<CarouselDelta.ModuleArrived>(deltas[0]).Module;
        Assert.Equal((Carousels.Entry, 0, 1, 90_000L, 90_000L), (arrived.Tag, arrived.ModuleId, arrived.Version, arrived.FirstSeen, arrived.LastSeen));
        CatalogModule listed = Assert.Single(Assert.Single(Assert.IsType<CarouselDelta.CatalogChanged>(deltas[1]).Catalog.Carousels).Modules);
        Assert.True(listed.Arrived);
        Assert.Equal(["startup.bml"], listed.Resources.Select(resource => resource.Path));
        Assert.True(state.Catalog?.CanOpen);
    }

    [Fact(DisplayName = "BR-BD-005: the same tag, module and version is held once")]
    public void TheSameVersionArrivingTwiceIsHeldOnce()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        Assert.Empty(state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 20));
        Assert.Single(state.Modules);
    }

    [Fact]
    public void AModuleTheCatalogDoesNotListIsNotHeld()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);

        Assert.Empty(state.Apply(Carousels.Completed(Carousels.Entry, 0, 2), 10));
        Assert.Empty(state.Apply(Carousels.Completed(Carousels.Entry, 7, 1), 10));
        Assert.Empty(state.Apply(Carousels.Completed(Carousels.Other, 0, 1), 10));
        Assert.Empty(state.Modules);
    }

    [Fact(DisplayName = "BR-BS-002: a module the new download info no longer lists leaves the catalog")]
    public void AModuleWithdrawnFromTheListingLeavesTheCatalog()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);
        state.Apply(Carousels.Completed(Carousels.Entry, 1, 1, "news.bml"), 10);

        IReadOnlyList<CarouselDelta> deltas = state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 20);

        CatalogCarousel carousel = Assert.Single(Assert.IsType<CarouselDelta.CatalogChanged>(Assert.Single(deltas)).Catalog.Carousels);
        Assert.Equal([0], carousel.Modules.Select(module => module.Id));
        Assert.Equal([(0, 1)], state.Modules.Select(module => (module.ModuleId, module.Version)));
    }

    [Fact(DisplayName = "BR-BS-002: a module whose version changed waits for the new version and the old one is no longer valid")]
    public void AModuleWhoseVersionChangedWaitsForTheNewVersion()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        state.Apply(Carousels.Listing(Carousels.Entry, 1, [0], (0, 2)), 20);

        Assert.Empty(state.Modules);
        CatalogModule waiting = Assert.Single(Assert.Single(state.Catalog!.Carousels).Modules);
        Assert.Equal((2, false), (waiting.Version, waiting.Arrived));
        Assert.Empty(state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 30));
        Assert.IsType<CarouselDelta.ModuleArrived>(state.Apply(Carousels.Completed(Carousels.Entry, 0, 2), 40)[0]);
    }

    [Fact(DisplayName = "BR-BS-002: a module superseded at the same version is put together again")]
    public void AModuleSupersededAtTheSameVersionIsPutTogetherAgain()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        state.Apply(Carousels.Listing(Carousels.Entry, 1, [0], (0, 1)), 20);

        Assert.Empty(state.Modules);
        Assert.IsType<CarouselDelta.ModuleArrived>(state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 30)[0]);
    }

    [Fact(DisplayName = "BR-BS-002: a module still listed at its version stays valid across a new listing and is seen again")]
    public void AModuleStillListedStaysValidAndIsSeenAgain()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        state.Apply(Carousels.Listing(Carousels.Entry, 1, [1], (0, 1), (1, 2)), 50);

        ModuleVersion kept = Assert.Single(state.Modules);
        Assert.Equal((0, 1, 10L, 50L), (kept.ModuleId, kept.Version, kept.FirstSeen, kept.LastSeen));
    }

    [Fact]
    public void ANewDownloadStartsAfresh()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, 1, [], (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);

        state.Apply(Carousels.Listing(Carousels.Entry, 2, [], (0, 1)), 20);

        Assert.Empty(state.Modules);
        Assert.Equal(2u, Assert.Single(state.Catalog!.Carousels).DownloadId);
    }

    [Fact]
    public void TheCatalogHoldsEachCarouselInOrderOfItsTag()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Other, (0, 1)), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);

        Assert.Equal([Carousels.Entry, Carousels.Other], state.Catalog!.Carousels.Select(carousel => carousel.Tag));
    }

    [Fact]
    public void ModulesThatArriveBeforeTheProgrammeMapIsReadAreHeldAndListedOnceItIs()
    {
        CarouselState state = new();
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1)), 0);

        IReadOnlyList<CarouselDelta> arrived = state.Apply(Carousels.Completed(Carousels.Entry, 0, 1), 10);
        IReadOnlyList<CarouselDelta> carried = state.Apply(Carousels.Carried(), 20);

        Assert.IsType<CarouselDelta.ModuleArrived>(Assert.Single(arrived));
        Assert.True(Assert.IsType<CarouselDelta.CatalogChanged>(Assert.Single(carried)).Catalog.CanOpen);
    }

    [Fact(DisplayName = "BR-BD-004: an event message comes through as it was timed")]
    public void AnEventMessageComesThrough()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        EventMessage message = Carousels.Event(7, 123_456);

        IReadOnlyList<CarouselDelta> deltas = state.Apply(new CarouselSignal.EventTimed(message), 100);

        Assert.Same(message, Assert.IsType<CarouselDelta.EventCame>(Assert.Single(deltas)).Message);
    }

    [Fact(DisplayName = "BR-BV-002: a carousel dropped again for the same reason is told once")]
    public void ACarouselDroppedAgainForTheSameReasonIsToldOnce()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);

        IReadOnlyList<CarouselDelta> first = state.Apply(new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TooManyModules), 0);
        IReadOnlyList<CarouselDelta> again = state.Apply(new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TooManyModules), 10);
        IReadOnlyList<CarouselDelta> otherReason = state.Apply(new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TotalTooLarge), 20);

        Assert.Equal(
            new CarouselDelta.CarouselDropped(Carousels.Other, CarouselDropReason.TooManyModules),
            Assert.Single(first));
        Assert.Empty(again);
        Assert.Equal(
            new CarouselDelta.CarouselDropped(Carousels.Other, CarouselDropReason.TotalTooLarge),
            Assert.Single(otherReason));
    }

    [Fact(DisplayName = "BR-BV-002: a carousel dropped leaves the catalog, and is told again once it has been listed since")]
    public void ACarouselDroppedLeavesTheCatalogAndIsToldAgainAfterItWasListed()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Other, (0, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Other, 0, 1), 10);

        IReadOnlyList<CarouselDelta> dropped = state.Apply(new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TotalTooLarge), 20);
        state.Apply(Carousels.Listing(Carousels.Other, (0, 1)), 30);
        IReadOnlyList<CarouselDelta> again = state.Apply(new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TotalTooLarge), 40);

        Assert.IsType<CarouselDelta.CarouselDropped>(dropped[0]);
        Assert.Empty(Assert.IsType<CarouselDelta.CatalogChanged>(dropped[1]).Catalog.Carousels);
        Assert.Empty(state.Modules);
        Assert.IsType<CarouselDelta.CarouselDropped>(again[0]);
    }

    [Fact]
    public void ACatalogWithoutTheStartupModuleCannotBeOpened()
    {
        CarouselState state = new();
        state.Apply(Carousels.Carried(), 0);
        state.Apply(Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0);
        state.Apply(Carousels.Completed(Carousels.Entry, 1, 1), 10);

        Assert.False(state.Catalog!.CanOpen);
    }
}
