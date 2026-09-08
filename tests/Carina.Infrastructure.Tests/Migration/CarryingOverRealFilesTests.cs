using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class CarryingOverRealFilesTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string from = Directory.CreateTempSubdirectory("carina-carrying-from").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-carrying-into").FullName;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly HeldMigrationRecords records = new();

    private readonly OneAtATime lease = new();

    private readonly HeldMigratedRecordings recordings;

    private readonly MigrationBench bench;

    public CarryingOverRealFilesTests()
    {
        bench = new MigrationBench(clock);
        recordings = new HeldMigratedRecordings(bench.Journal);

        File.WriteAllText(Path.Combine(from, "one.m2ts"), "a recording");
        File.WriteAllText(Path.Combine(from, "bash.sh"), "not a recording at all");
        File.WriteAllText(Path.Combine(from, "empty.m2ts"), string.Empty);
    }

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact]
    public async Task ARehearsalLeavesBothSidesExactlyAsItFoundThemHoweverOftenItIsRun()
    {
        IReadOnlyList<string> before = Walked(from);

        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(before, Walked(from));
        Assert.Empty(Directory.GetFileSystemEntries(into));
        Assert.Empty(recordings.Written);
        Assert.Equal(3, records.Saved.Count);
    }

    [Fact]
    public async Task WhatIsCarriedArrivesAsALinkAndTheSourceIsUntouched()
    {
        IReadOnlyList<string> before = Walked(from);

        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Recording written = recordings.Written.Single();

        Assert.Equal(before, Walked(from));
        Assert.Equal([written.FileName.Value], Directory.GetFiles(into).Select(Path.GetFileName));

        await File.AppendAllTextAsync(Path.Combine(from, "one.m2ts"), " that grew", Cancel);

        Assert.Equal(
            "a recording that grew",
            await File.ReadAllTextAsync(Path.Combine(into, written.FileName.Value), Cancel));
    }

    [Fact]
    public async Task NothingTheRunRefusedEverAppearsInTheNewRoot()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(Directory.GetFiles(into));
        Assert.DoesNotContain(
            Directory.GetFiles(into).Select(Path.GetFileName),
            name => name is "bash.sh" or "empty.m2ts");
    }

    [Fact]
    public async Task EmptyingTheNewRootAndForgettingTheRowsIsAllItTakesToStartAgain()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(recordings.Written);

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Contains("delete the new root", stopped.Message, StringComparison.Ordinal);

        foreach (string carried in Directory.GetFiles(into))
        {
            File.Delete(carried);
        }

        recordings.Written.Clear();

        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(recordings.Written);
        Assert.Single(Directory.GetFiles(into));
    }

    private static IReadOnlyList<string> Walked(string directory)
        => [.. Directory
            .GetFiles(directory)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetFileName(file)} {new FileInfo(file).Length} {File.GetLastWriteTimeUtc(file):O}")];

    private MigrationPassage Passage()
        => new(
            new SourceOnDisk(from),
            new SourceOnDisk(from),
            bench.Carriage(new HardLinkMigrationCarrier(from, into, Root), recordings),
            records,
            lease,
            clock);
}

internal sealed class SourceOnDisk(string directory) : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Ledger(
            [Recording(7), Recording(8)],
            [AsBroadcast(7, "one.m2ts", 11), AsBroadcast(8, "empty.m2ts", 4096)]));

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. Directory
                .GetFiles(directory)
                .Order(StringComparer.Ordinal)
                .Select(file => OnDisk(Path.GetFileName(file), new FileInfo(file).Length))]);
}
