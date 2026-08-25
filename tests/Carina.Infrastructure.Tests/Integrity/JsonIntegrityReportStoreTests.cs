using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class JsonIntegrityReportStoreTests
{
    private static readonly DateTime At = new(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly RecordingFileName Name = new("one.m2ts");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    [Fact]
    public async Task AReportKeepsEveryClassOfFindingAndEverythingItSaysAboutThem()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        IntegritySweep written = IntegritySweep.Of(
            At,
            2,
            1,
            4,
            5,
            3,
            1,
            1,
            [
                IntegrityFinding.SizeDisagrees(Primary, Id(1), Name, 100, 99, At),
                IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", 512, At),
                IntegrityFinding.FileMissing(Primary, Id(3), Name, 100, At),
                IntegrityFinding.FileEmpty(Primary, Id(4), Name, 100, 0, At),
            ]);

        await store.SaveAsync(written, Cancel);
        IntegritySweep read = Assert.IsType<IntegritySweep>(await store.LatestAsync(Cancel));

        Assert.True(File.Exists(path));
        Assert.Equal(At, read.RanAt);
        Assert.Equal(2, read.RootsWalked);
        Assert.Equal(1, read.RootsOutOfReach);
        Assert.Equal(4, read.FilesRead);
        Assert.Equal(5, read.LedgerRowsRead);
        Assert.Equal(3, read.LedgerRowsJudged);
        Assert.Equal(1, read.LedgerRowsStillWriting);
        Assert.Equal(1, read.LedgerRowsInRootsOutOfReach);
        Assert.Equal(written.Findings, read.Findings);
    }

    [Fact]
    public async Task AReportOfNothingIsStillAReport()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out _);

        await store.SaveAsync(IntegritySweep.Of(At, 1, 0, 2, 3, 3, 0, 0, []), Cancel);
        IntegritySweep read = Assert.IsType<IntegritySweep>(await store.LatestAsync(Cancel));

        Assert.Empty(read.Findings);
        Assert.Equal(2, read.FilesRead);
        Assert.Equal(3, read.LedgerRowsJudged);
    }

    [Fact]
    public async Task NothingHasBeenWrittenYetSoThereIsNoLatestReport()
    {
        using var tree = new TempTree();

        Assert.Null(await Store(tree, out _).LatestAsync(Cancel));
    }

    [Fact]
    public async Task TheNewestReportIsTheOneThatComesBack()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out _);

        await store.SaveAsync(IntegritySweep.Of(At, 1, 0, 0, 0, 0, 0, 0, []), Cancel);
        await store.SaveAsync(IntegritySweep.Of(At.AddHours(1), 9, 0, 0, 0, 0, 0, 0, []), Cancel);

        IntegritySweep read = Assert.IsType<IntegritySweep>(await store.LatestAsync(Cancel));

        Assert.Equal(At.AddHours(1), read.RanAt);
        Assert.Equal(9, read.RootsWalked);
    }

    [Fact]
    public async Task AReportLeavesNothingHalfWrittenBehindIt()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await store.SaveAsync(IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, []), Cancel);

        Assert.Equal(
            [Path.GetFileName(path)],
            Directory.EnumerateFiles(Path.GetDirectoryName(path)!)
                .Select(file => Path.GetFileName(file))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task AReportMakesTheDirectoryItIsKeptIn()
    {
        using var tree = new TempTree();
        var store = new JsonIntegrityReportStore(
            new IntegritySettings { ReportPath = tree.Under("nested", "deeper", "report.json") });

        await store.SaveAsync(IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, []), Cancel);

        Assert.True(File.Exists(tree.Under("nested", "deeper", "report.json")));
    }

    [Fact]
    public async Task AReportNobodyHandedOverIsRefused()
    {
        using var tree = new TempTree();

        await Assert.ThrowsAsync<ArgumentNullException>(() => Store(tree, out _).SaveAsync(null!, Cancel));
    }

    [Fact]
    public async Task AReportWrittenAgainstAnotherSchemaIsRefused()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await store.SaveAsync(IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, []), Cancel);
        await File.WriteAllTextAsync(
            path,
            (await File.ReadAllTextAsync(path, Cancel)).Replace(
                "\"Schema\": 1",
                "\"Schema\": 2",
                StringComparison.Ordinal),
            Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync(Cancel));
    }

    [Fact]
    public async Task AReportNamingAClassNobodyHoldsIsRefused()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await store.SaveAsync(
            IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, [IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", 1, At)]),
            Cancel);
        await File.WriteAllTextAsync(
            path,
            (await File.ReadAllTextAsync(path, Cancel)).Replace(
                "\"NoLedgerRow\"",
                "\"SomethingElse\"",
                StringComparison.Ordinal),
            Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync(Cancel));
    }

    [Fact]
    public async Task AFindingThatSaysNothingAboutTheSizeItSawIsRefused()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await store.SaveAsync(
            IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, [IntegrityFinding.NoLedgerRow(Primary, "stray.m2ts", 1, At)]),
            Cancel);
        await File.WriteAllTextAsync(
            path,
            (await File.ReadAllTextAsync(path, Cancel)).Replace(
                "\"ObservedSize\": 1",
                "\"ObservedSize\": null",
                StringComparison.Ordinal),
            Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync(Cancel));
    }

    [Fact]
    public async Task AFindingThatNamesNoRecordingWhereOneIsNeededIsRefused()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await store.SaveAsync(
            IntegritySweep.Of(At, 0, 0, 0, 0, 0, 0, 0, [IntegrityFinding.FileMissing(Primary, Id(3), Name, 100, At)]),
            Cancel);
        await File.WriteAllTextAsync(
            path,
            (await File.ReadAllTextAsync(path, Cancel)).Replace(
                $"\"RecordingId\": \"{Id(3).Value}\"",
                "\"RecordingId\": null",
                StringComparison.Ordinal),
            Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync(Cancel));
    }

    [Fact]
    public async Task AReportHoldingNothingAtAllIsRefused()
    {
        using var tree = new TempTree();
        JsonIntegrityReportStore store = Store(tree, out string path);

        await File.WriteAllTextAsync(path, "null", Cancel);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync(Cancel));
    }

    private static JsonIntegrityReportStore Store(TempTree tree, out string path)
    {
        path = tree.Under("report.json");

        return new JsonIntegrityReportStore(new IntegritySettings { ReportPath = path });
    }
}
