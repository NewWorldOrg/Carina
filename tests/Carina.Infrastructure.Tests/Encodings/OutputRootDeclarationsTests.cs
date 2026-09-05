using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Thumbnails;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class OutputRootDeclarationsTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly StorageRootDto Primary = new() { Name = "primary", FreeBytes = 10, TotalBytes = 20, Writable = true };

    [Fact(DisplayName = "BR-EV-001: the roots this process holds are declared after the driver's, measured on this machine")]
    public async Task TheRootsThisProcessHoldsAreDeclaredAfterTheDriversMeasuredOnThisMachine()
    {
        using var shelf = new TempTree();
        OutputRootDeclarations declarations = Declaring([Primary], shelf.Root, new DirectoryRenameProbe());

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await declarations.ReadAsync(Cancel);

        Assert.True(answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared));
        Assert.Equal(["primary", "encodes"], declared.Select(root => root.Name));
        Assert.True(declared[1].Writable);
        Assert.True(declared[1].TotalBytes > 0);
        Assert.True(declared[1].FreeBytes > 0);
        Assert.True(declared[1].FreeBytes <= declared[1].TotalBytes);
        Assert.Empty(shelf.Snapshot());
    }

    [Fact(DisplayName = "BR-EV-001: a held root a rename does not land in is declared as not writable")]
    public async Task AHeldRootARenameDoesNotLandInIsDeclaredAsNotWritable()
    {
        using var shelf = new TempTree();
        OutputRootDeclarations declarations = Declaring(
            [Primary],
            shelf.Root,
            new ScriptedProbe(new RenameVerdict(RenameStanding.CannotWriteTo, "Read-only file system")));

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await declarations.ReadAsync(Cancel);

        Assert.True(answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared));
        Assert.False(declared[1].Writable);
        Assert.True(declared[1].TotalBytes > 0);
    }

    [Fact]
    public async Task AHeldRootThatIsNotThereIsDeclaredUnmeasured()
    {
        using var shelf = new TempTree();
        OutputRootDeclarations declarations = Declaring([Primary], shelf.Under("nowhere"), new DirectoryRenameProbe());

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await declarations.ReadAsync(Cancel);

        Assert.True(answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared));
        Assert.Equal(0, declared[1].TotalBytes);
        Assert.False(declared[1].Writable);
    }

    [Fact(DisplayName = "BR-EV-001: a held root named like one the driver declares is left out, and the warning names it")]
    public async Task AHeldRootNamedLikeOneTheDriverDeclaresIsLeftOut()
    {
        using var shelf = new TempTree();
        var log = new HeardOf<OutputRootDeclarations>();
        OutputRootDeclarations declarations = Declaring([Primary], shelf.Root, new DirectoryRenameProbe(), "primary", log);

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await declarations.ReadAsync(Cancel);

        Assert.True(answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared));
        Assert.Equal(Primary, Assert.Single(declared));
        string warning = Assert.Single(log.Warnings);
        Assert.Contains("primary", warning, StringComparison.Ordinal);
        Assert.Contains("left out", warning, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-EV-001: a driver that cannot be reached leaves the set unanswered rather than answered with half of it")]
    public async Task ADriverThatCannotBeReachedLeavesTheSetUnanswered()
    {
        using var shelf = new TempTree();
        var driver = new ScriptedDriverClient
        {
            StorageAnswer = DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable("no socket at that path"),
        };
        OutputRootDeclarations declarations = Declaring(driver, shelf.Root, new DirectoryRenameProbe(), "encodes", new HeardOf<OutputRootDeclarations>());

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await declarations.ReadAsync(Cancel);

        Assert.Equal(DriverCallOutcome.Unreachable, answer.Outcome);
        Assert.Equal("no socket at that path", answer.Failure);
    }

    private static OutputRootDeclarations Declaring(
        IReadOnlyList<StorageRootDto> driverDeclares,
        string shelf,
        IRenameProbe probe,
        string held = "encodes",
        HeardOf<OutputRootDeclarations>? log = null)
        => Declaring(
            new ScriptedDriverClient { StorageAnswer = DriverCall<IReadOnlyList<StorageRootDto>>.Reached(driverDeclares) },
            shelf,
            probe,
            held,
            log ?? new HeardOf<OutputRootDeclarations>());

    private static OutputRootDeclarations Declaring(
        ScriptedDriverClient driver,
        string shelf,
        IRenameProbe probe,
        string held,
        HeardOf<OutputRootDeclarations> log)
        => new(
            new StorageMonitor(driver, TimeProvider.System, StorageMonitorSettings.Default),
            new EncodeSettings { OutputRoots = [new StorageRootPath(new OutputRoot(held), shelf)] },
            probe,
            log);
}
