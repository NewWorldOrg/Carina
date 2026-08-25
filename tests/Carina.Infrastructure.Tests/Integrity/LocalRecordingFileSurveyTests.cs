using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class LocalRecordingFileSurveyTests
{
    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheSurveyOffersTheRootsItWasToldAbout()
    {
        using var tree = new TempTree();
        LocalRecordingFileSurvey survey = Survey(new StorageRootPath(Primary, tree.Root));

        Assert.Equal(["primary"], (await survey.RootsAsync(Cancel)).Select(root => root.Value).ToArray());
    }

    [Fact]
    public async Task TheSurveyOffersNoRootWhenNothingIsMounted()
    {
        Assert.Empty(await Survey().RootsAsync(Cancel));
    }

    [Fact]
    public async Task AWalkReadsTheNameAndTheSizeOfEveryFile()
    {
        using var tree = new TempTree();
        tree.Holding("one.m2ts", 100).Holding("two.m2ts", 3);

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Primary, Cancel);

        Assert.True(listing.Reachable);
        Assert.Equal(100, listing.Named("one.m2ts")?.SizeBytes);
        Assert.Equal(3, listing.Named("two.m2ts")?.SizeBytes);
        Assert.Equal(2, listing.Files.Count);
    }

    [Fact]
    public async Task AWalkReadsAFileHoldingNothingAsHoldingNothing()
    {
        using var tree = new TempTree();
        tree.Holding("one.m2ts", 0);

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Primary, Cancel);

        Assert.Equal(0, listing.Named("one.m2ts")?.SizeBytes);
    }

    [Fact]
    public async Task AWalkOverAnEmptyRootComesBackReachableAndEmpty()
    {
        using var tree = new TempTree();

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Primary, Cancel);

        Assert.True(listing.Reachable);
        Assert.Empty(listing.Files);
    }

    [Fact]
    public async Task AWalkPassesOverDirectoriesAndWhatIsInsideThem()
    {
        using var tree = new TempTree();
        tree.Holding("one.m2ts", 100).HoldingDirectory("thumbnails");
        File.WriteAllBytes(tree.Under("thumbnails", "one.jpg"), new byte[7]);

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Primary, Cancel);

        Assert.Equal(["one.m2ts"], listing.Files.Select(file => file.Name).ToArray());
    }

    [Fact]
    public async Task ARootNobodyMountedIsOutOfReachRatherThanEmpty()
    {
        using var tree = new TempTree();

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Bulk, Cancel);

        Assert.False(listing.Reachable);
        Assert.Equal("bulk", listing.Root.Value);
    }

    [Fact]
    public async Task ARootMountedWhereThereIsNoDirectoryIsOutOfReachRatherThanEmpty()
    {
        using var tree = new TempTree();

        RootListing listing = await Survey(new StorageRootPath(Primary, tree.Under("gone")))
            .ListAsync(Primary, Cancel);

        Assert.False(listing.Reachable);
    }

    [Fact]
    public async Task AWalkOfNoRootAtAllIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Survey().ListAsync(null!, Cancel));
    }

    [Fact]
    public async Task AWalkThatWasCalledOffIsNotWalked()
    {
        using var tree = new TempTree();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Survey(new StorageRootPath(Primary, tree.Root)).ListAsync(Primary, cancelled.Token));
    }

    private static LocalRecordingFileSurvey Survey(params StorageRootPath[] mounted)
        => new(
            new IntegritySettings { OutputRoots = mounted },
            NullLogger<LocalRecordingFileSurvey>.Instance);
}
