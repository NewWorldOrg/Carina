using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeProfileRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task AProfileWrittenDownIsFoundAgainAsItWasWritten()
    {
        await ClearAsync();
        (EncodeProfile profile, _) = await DefinedAsync();

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeProfile? readProfile = await Profiles(reading).FindAsync(profile.Id, Cancel);

        Assert.NotNull(readProfile);
        Assert.Equal(new EncodeLabel("Viewing"), readProfile.Label);
        Assert.Equal(EncodeCodec.H264, readProfile.Codec);
        Assert.Equal(EncodeResolution.Hd, readProfile.Resolution);
        Assert.Equal(Deinterlace.EveryFrame, readProfile.Deinterlace);
        Assert.Equal(new ConstantRateFactor(22), readProfile.SoftwareRateControl);
        Assert.Equal(new ConstantQuantiser(25), readProfile.VaapiRateControl);
        Assert.Equal(Defined, readProfile.DefinedAt);
        Assert.Contains(await Profiles(reading).ListAsync(Cancel), listed => listed.Id.Equals(profile.Id));
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    private async Task<(EncodeProfile, EncodeDestination)> DefinedAsync()
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            Defined);
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Primary"),
            Primary,
            profile.Id,
            Defined);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);

        return (profile, destination);
    }

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
