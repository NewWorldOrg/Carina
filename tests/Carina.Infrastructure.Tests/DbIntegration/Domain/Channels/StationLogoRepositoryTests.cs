using Carina.Domain.Channels;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Channels;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class StationLogoRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task ALogoComesBackWithEveryByteOfThePictureItWentInWith()
    {
        int network = BroadcastIds.NextNetwork();
        byte[] picture = Picture(2048);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Logos(scope).AbsorbAsync(Logo(network, picture: picture), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        StationLogo? found = await Logos(reading)
            .FindAsync(new NetworkId(network), new LogoId(261), Cancel);

        Assert.NotNull(found);
        Assert.Equal(picture, found.Picture);
        Assert.Equal(64, found.Width);
        Assert.Equal(36, found.Height);
        Assert.Equal(At, found.CollectedAt);
    }

    [Fact]
    public async Task ALargerDrawingOfTheSameLogoReplacesTheOneAlreadyKept()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IStationLogoRepository logos = Logos(scope);
        await logos.AbsorbAsync(Logo(network, width: 48, height: 24), Cancel);

        await logos.AbsorbAsync(Logo(network, width: 64, height: 36, at: At.AddDays(1)), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        StationLogo? found = await Logos(reading)
            .FindAsync(new NetworkId(network), new LogoId(261), Cancel);

        Assert.Equal(64, found!.Width);
        Assert.Equal(At.AddDays(1), found.CollectedAt);
    }

    [Fact]
    public async Task ASmallerDrawingOfTheSameLogoLeavesTheOneAlreadyKeptWhereItIs()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IStationLogoRepository logos = Logos(scope);
        await logos.AbsorbAsync(Logo(network, width: 64, height: 36), Cancel);

        await logos.AbsorbAsync(Logo(network, width: 48, height: 24, at: At.AddDays(1)), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        StationLogo? found = await Logos(reading)
            .FindAsync(new NetworkId(network), new LogoId(261), Cancel);

        Assert.Equal(64, found!.Width);
        Assert.Equal(At, found.CollectedAt);
    }

    [Fact]
    public async Task TheLogoOfAServiceIsFoundThroughTheLogoThatServiceNames()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Logos(scope).AbsorbAsync(Logo(network), Cancel);
        IBroadcastServiceRepository services = Services(scope);
        await services.AddAsync(Naming(network, 1, new LogoId(261)), Cancel);
        await services.AddAsync(Naming(network, 2, new LogoId(261)), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IStationLogoRepository found = Logos(reading);

        Assert.Equal(
            new LogoId(261),
            (await found.OfServiceAsync(new NetworkId(network), new ServiceId(1), Cancel))?.LogoId);
        Assert.Equal(
            new LogoId(261),
            (await found.OfServiceAsync(new NetworkId(network), new ServiceId(2), Cancel))?.LogoId);
    }

    [Fact]
    public async Task AServiceThatNamesNoLogoIsAnsweredWithNothingRatherThanSomeoneElsesLogo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Logos(scope).AbsorbAsync(Logo(network), Cancel);
        await Services(scope).AddAsync(Naming(network, 1, null), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await Logos(reading)
            .OfServiceAsync(new NetworkId(network), new ServiceId(1), Cancel));
    }

    [Fact]
    public async Task AServiceThatNamesALogoNobodyHasCollectedYetIsAnsweredWithNothing()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Services(scope).AddAsync(Naming(network, 1, new LogoId(261)), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await Logos(reading)
            .OfServiceAsync(new NetworkId(network), new ServiceId(1), Cancel));
    }

    private static byte[] Picture(int length)
    {
        byte[] picture = new byte[length];

        for (int at = 0; at < length; at++)
        {
            picture[at] = (byte)(at & 0xFF);
        }

        return picture;
    }

    private static StationLogo Logo(
        int network,
        int width = 64,
        int height = 36,
        byte[]? picture = null,
        DateTime? at = null)
        => StationLogo.Collect(
            new NetworkId(network),
            new LogoId(261),
            0x05,
            3,
            width,
            height,
            picture ?? [0x89, 0x50, 0x4E, 0x47],
            at ?? At);

    private static BroadcastService Naming(int network, int service, LogoId? logoId)
        => BroadcastService.Rehydrate(
            new NetworkId(network),
            new ServiceId(service),
            "Fixture Service",
            ServiceCategory.Television,
            At,
            At,
            logoId: logoId,
            logoDeclaration: logoId is null
                ? StationLogoDeclaration.NoPictureIsBroadcast
                : StationLogoDeclaration.InTheCommonDataTable);

    private static IStationLogoRepository Logos(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IStationLogoRepository>();

    private static IBroadcastServiceRepository Services(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IBroadcastServiceRepository>();
}
