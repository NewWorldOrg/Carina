using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

public sealed class ProgrammeSearchScopeTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ABroadcastTypeBecomesTheServicesThatTypeCarries()
    {
        ProgrammeSearchScope scope = Scope(Terrestrial(4, 1049), Satellite(4, 1032));

        ProgrammeSearch bound = await scope.BoundAsync(Asking(TuneSystem.IsdbT), Cancel);

        Assert.Equal([new ProgrammeService(4, 1049)], bound.Services);
        Assert.Empty(bound.Withheld);
    }

    [Fact]
    public async Task ABroadcastTypeThatCarriesNothingNamesNoServiceRatherThanEveryService()
    {
        ProgrammeSearchScope scope = Scope(Terrestrial(4, 1049));

        ProgrammeSearch bound = await scope.BoundAsync(Asking(TuneSystem.IsdbSCs110), Cancel);

        Assert.NotNull(bound.Services);
        Assert.Empty(bound.Services);
    }

    [Fact]
    public async Task WhatTheGuideDoesNotListIsLeftOutOfASearchThatNamesNoBroadcastType()
    {
        ProgrammeSearchScope scope = Scope(
            [Terrestrial(4, 1049)],
            Listed(4, 1049),
            Unlisted(4, 1032));

        ProgrammeSearch bound = await scope.BoundAsync(Asking(null), Cancel);

        Assert.Null(bound.Services);
        Assert.Equal([new ProgrammeService(4, 1032)], bound.Withheld);
    }

    [Fact]
    public async Task WhatTheGuideDoesNotListIsLeftOutOfTheServicesABroadcastTypeCarriesToo()
    {
        ProgrammeSearchScope scope = Scope(
            [Terrestrial(4, 1049, 1032)],
            Listed(4, 1049),
            Unlisted(4, 1032));

        ProgrammeSearch bound = await scope.BoundAsync(Asking(TuneSystem.IsdbT), Cancel);

        Assert.Equal([new ProgrammeService(4, 1049)], bound.Services);
    }

    [Fact]
    public async Task TheStreamsOneBroadcastTypeCarriesLeaveOutWhatTheGuideDoesNotList()
    {
        ProgrammeSearchScope scope = Scope(
            [Terrestrial(4, 1049, 1032), Satellite(4, 1040)],
            Unlisted(4, 1032));

        IReadOnlyList<BroadcastStream> listed = await scope.ListedAsync(TuneSystem.IsdbT, Cancel);

        Assert.Equal([new ServiceId(1049)], Assert.Single(listed).Services);
    }

    private static ProgrammeSearch Asking(TuneSystem? system)
        => ProgrammeSearch.For(
            "news",
            null,
            null,
            conditions: new ProgrammeConditions { System = system })!;

    private static ProgrammeSearchScope Scope(params BroadcastStream[] streams)
        => Scope(streams, []);

    private static ProgrammeSearchScope Scope(
        IReadOnlyList<BroadcastStream> streams,
        params BroadcastService[] services)
    {
        var catalogue = new HeldServices();
        catalogue.Services.AddRange(services);

        return new ProgrammeSearchScope(new HeldStreams(streams), catalogue);
    }

    private static BroadcastService Listed(int network, int service)
        => BroadcastService.Discover(
            new NetworkId(network),
            new ServiceId(service),
            "listed",
            ServiceCategory.Television,
            At);

    private static BroadcastService Unlisted(int network, int service)
        => BroadcastService.Discover(
            new NetworkId(network),
            new ServiceId(service),
            "unlisted",
            ServiceCategory.OneSeg,
            At);

    private static BroadcastStream Terrestrial(int network, params int[] services)
        => new(
            new NetworkId(network),
            new TransportStreamId(32_736),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);

    private static BroadcastStream Satellite(int network, params int[] services)
        => new(
            new NetworkId(network),
            new TransportStreamId(32_737),
            TuningParameters.Bs(5, new TransportStreamId(32_737)),
            [.. services.Select(service => new ServiceId(service))]);
}
