using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Tests.Fixtures.Migration;
using Carina.TestSupport;

using static Carina.TestSupport.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Unit.Infra.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationPassageTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static string[] Carried => [MigrationOnRealFiles.Carried, MigrationOnRealFiles.AlsoCarried];

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly ScriptedCarrier carrier;

    private readonly HeldMigratedRecordings recordings;

    private readonly HeldMigrationRecords records = new();

    private readonly OneAtATime lease = new();

    private readonly ReadOnlySource readOnlySource = new();

    private readonly MigrationBench bench;

    private readonly MigrationJournal journal;

    private readonly string from = Directory.CreateTempSubdirectory("carina-carrying-from").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-carrying-into").FullName;

    private readonly ARunOverRealFiles run = new();

    public MigrationPassageTests()
    {
        bench = new MigrationBench(clock);
        journal = bench.Journal;
        carrier = new ScriptedCarrier(journal);
        recordings = new HeldMigratedRecordings(journal);

        File.WriteAllText(Path.Combine(from, "one.m2ts"), "a recording");
        File.WriteAllText(Path.Combine(from, "notes.txt"), "not a recording at all");
        File.WriteAllText(Path.Combine(from, "empty.m2ts"), string.Empty);
    }

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
        run.Dispose();
    }

    [Fact]
    public async Task ASecondStartWhileOneIsRunningIsRefused()
    {
        lease.Held = true;

        MigrationAlreadyRunningException refused = await Assert.ThrowsAsync<MigrationAlreadyRunningException>(
            () => Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Contains("already running", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, readOnlySource.Reads);
        Assert.Empty(journal.Steps);
        Assert.Empty(records.Saved);
    }

    [Fact]
    public async Task TheLeaseIsGivenBackWhenTheRunIsOver()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Equal(1, lease.Taken);
        Assert.Equal(1, lease.Returned);
    }

    [Fact]
    public async Task TheLeaseIsGivenBackWhenTheRunStops()
    {
        carrier.Answers(
            "one.m2ts",
            new MigrationCarry(
                MigrationCarryOutcome.NotOnTheSameFilesystem,
                "a hard link cannot cross filesystems"));

        await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Equal(1, lease.Returned);
        Assert.Empty(records.Saved);
    }

    [Fact]
    public async Task ARehearsalReadsTheSameThingsAndChangesNothing()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(1, readOnlySource.Reads);
        Assert.Equal(1, readOnlySource.Walks);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARehearsalIsWrittenDownAsARehearsal()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(MigrationPass.Rehearsal, records.Saved.Single().Run.Pass);
        Assert.Equal(Source, records.Saved.Single().Run.Source);
    }

    [Fact]
    public async Task ARehearsalCanBeRunAgainAndAgain()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(3, records.Saved.Count);
        Assert.Empty(journal.Steps);
        Assert.Equal(3, lease.Returned);
    }

    [Fact]
    public async Task ARunForRealCarriesAndSaysSo()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Equal(MigrationPass.ForReal, records.Saved.Single().Run.Pass);
        Assert.Single(recordings.Written);
        Assert.Equal(["link one.m2ts", $"row {carrier.Named.Single()}"], journal.Steps);
    }

    [Fact]
    public async Task ARehearsalLeavesBothSidesExactlyAsItFoundThemHoweverOftenItIsRun()
    {
        IReadOnlyList<string> before = Walked(from);

        await PassageOverFiles().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await PassageOverFiles().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await PassageOverFiles().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(before, Walked(from));
        Assert.Empty(Directory.GetFileSystemEntries(into));
        Assert.Empty(recordings.Written);
        Assert.Equal(3, records.Saved.Count);
    }

    [Fact]
    public async Task WhatIsCarriedArrivesAsALinkAndTheSourceIsUntouched()
    {
        IReadOnlyList<string> before = Walked(from);

        await PassageOverFiles().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

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
        await PassageOverFiles().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(Directory.GetFiles(into));
        Assert.DoesNotContain(
            Directory.GetFiles(into).Select(Path.GetFileName),
            name => name is "notes.txt" or "empty.m2ts");
    }

    [Fact]
    public async Task EmptyingTheNewRootAndForgettingTheRowsIsAllItTakesToStartAgain()
    {
        await PassageOverFiles().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(recordings.Written);

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => PassageOverFiles().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Contains("take out of the new root", stopped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("delete the new root", stopped.Message, StringComparison.Ordinal);

        foreach (string carried in Directory.GetFiles(into))
        {
            File.Delete(carried);
        }

        recordings.Written.Clear();

        await PassageOverFiles().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Single(recordings.Written);
        Assert.Single(Directory.GetFiles(into));
    }

    private static IReadOnlyList<string> Walked(string directory)
        => [.. Directory
            .GetFiles(directory)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetFileName(file)} {new FileInfo(file).Length} {File.GetLastWriteTimeUtc(file):O}")];

    [Fact]
    public async Task ANewRootThatWentAwayAfterItWasLookedAtIsWrittenDownRatherThanThrownOver()
    {
        string gone = Path.Combine(into, "gone-by-then");
        Directory.CreateDirectory(gone);
        Directory.Delete(gone);

        await PassageOverFiles(gone).RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        MigrationReport written = records.Saved.Single();

        Assert.Equal(
            MigrationFinding.TheNewRootIsNotThere,
            Found(written, MigrationStandingSubject.TheNewRoot));
        Assert.Equal(
            MigrationFinding.TheNewRootDoesNotTakeALink,
            Found(written, MigrationStandingSubject.CarryingIntoTheNewRoot));
    }

    [Fact]
    public async Task WhatARehearsalForetellsAboutTheLinkIsWrittenDownWithTheRest()
    {
        await PassageOverFiles().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(
            MigrationFinding.TheCarryWouldBeAHardLink,
            Found(records.Saved.Single(), MigrationStandingSubject.CarryingIntoTheNewRoot));
    }

    private static MigrationFinding Found(MigrationReport written, MigrationStandingSubject subject)
        => written.Standings.Single(standing => standing.Subject == subject).Finding;

    [Fact]
    public async Task ARunThatDiesBeforeItLinksAnythingLeavesNoRowAndNoFileBehind()
    {
        run.Source.WhenWalking = new IOException("the machine went down before the run had read the disk");

        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        Assert.Empty(run.Records.Saved);
        Assert.Empty(run.Recordings.Written);
        Assert.Empty(run.Bench.Rules.Rules);
        Assert.Empty(run.Bench.Jobs.Jobs);
        Assert.Empty(Directory.GetFileSystemEntries(run.Into));
        Assert.Equal(run.Lease.Taken, run.Lease.Returned);
    }

    [Fact]
    public async Task ARunThatDiesBetweenALinkAndItsRowWritesNoRecordOfItself()
    {
        Interrupt(after: 1);

        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        Assert.Empty(run.Records.Saved);
        Assert.Single(run.Recordings.Written);
        Assert.Equal(2, Directory.GetFiles(run.Into).Length);
        Assert.Equal(run.Lease.Taken, run.Lease.Returned);
    }

    [Fact]
    public async Task EmptyingTheNewRootAndForgettingTheRowsIsAllItTakesAfterARunWasInterrupted()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        Interrupt(after: 1);
        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        run.Recordings.WhenAdding = null;
        run.Recordings.Written.Clear();
        run.Bench.Rules.Rules.Clear();
        run.Bench.Jobs.Jobs.Clear();

        foreach (string stray in Directory.GetFiles(run.Into))
        {
            File.Delete(stray);
        }

        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> source = WhatTheFilesystemSays.Under(run.From);
        IReadOnlyList<FileStanding> arrived = WhatTheFilesystemSays.Under(run.Into);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.Single(run.Records.Saved);
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(source));
        Assert.Equal(
            arrived.Select(file => file.Inode).Order(),
            source.Where(file => file.Links is 2).Select(file => file.Inode).Order());
        Assert.Equal(
            run.Recordings.Written.Select(recording => recording.FileName.Value).Order(StringComparer.Ordinal),
            arrived.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WhatWasCarriedIsOneFileUnderTwoNamesRatherThanTwoFiles()
    {
        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> source = WhatTheFilesystemSays.Under(run.From);
        IReadOnlyList<FileStanding> arrived = WhatTheFilesystemSays.Under(run.Into);

        Assert.Equal(2, arrived.Count);

        foreach (string name in Carried)
        {
            FileStanding original = WhatTheFilesystemSays.Named(source, name);
            FileStanding link = arrived.Single(file => file.Inode == original.Inode);

            Assert.Equal(2, original.Links);
            Assert.Equal(2, link.Links);
            Assert.Equal(original.Size, link.Size);
            Assert.Equal(original.Digest, link.Digest);
            Assert.NotEqual(original.Name, link.Name);
        }

        Assert.Equal(1, WhatTheFilesystemSays.Named(source, MigrationOnRealFiles.NotARecording).Links);
        Assert.Equal(1, WhatTheFilesystemSays.Named(source, MigrationOnRealFiles.NothingLandedIn).Links);
    }

    [Fact]
    public async Task TheDiskHoldsNoMoreBlocksAfterARunThanItHeldBeforeIt()
    {
        long before = Held();

        await run.RunAsync(MigrationPass.ForReal);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.True(before > 0, "A run that carried nothing off a disk holding nothing would prove nothing.");
        Assert.Equal(before, Held());
    }

    [Fact]
    public async Task ARunForRealRenamesCopiesAndDeletesNothingAndChangesNotOneByte()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> after = WhatTheFilesystemSays.Under(run.From);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.Equal(4, after.Count);
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(after));
    }

    [Fact]
    public async Task ARehearsalRenamesCopiesAndDeletesNothingAndChangesNotOneByte()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        await run.RunAsync(MigrationPass.Rehearsal);

        IReadOnlyList<FileStanding> after = WhatTheFilesystemSays.Under(run.From);

        Assert.Single(run.Records.Saved);
        Assert.Empty(run.Recordings.Written);
        Assert.Empty(Directory.GetFileSystemEntries(run.Into));
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(after));
        Assert.Equal(before.Select(file => file.Links), after.Select(file => file.Links));
    }

    private MigrationPassage Passage()
        => new(
            readOnlySource,
            readOnlySource,
            bench.Carriage(carrier, recordings),
            records,
            lease,
            clock);

    private MigrationPassage PassageOverFiles() => PassageOverFiles(into);

    private MigrationPassage PassageOverFiles(string root)
        => new(
            new SourceOnDisk(from),
            new SourceOnDisk(from),
            bench.Carriage(new HardLinkMigrationCarrier(from, root, Root), recordings),
            records,
            lease,
            clock);

    private void Interrupt(int after)
    {
        run.Recordings.WrittenBeforeItRefuses = after;
        run.Recordings.WhenAdding = new IOException("the machine went down part way through the run");
    }

    private long Held()
        => WhatTheFilesystemSays.BytesTheDiskHoldsFor(
            WhatTheFilesystemSays.Under(run.From),
            WhatTheFilesystemSays.Under(run.Into));
}

internal sealed class ReadOnlySource : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public int Reads { get; private set; }

    public int Walks { get; private set; }

    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Ledger([Recording(7)], [AsBroadcast(7, "one.m2ts", 100)]));
    }

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        Walks++;

        return Task.FromResult<IReadOnlyList<SourceFile>>([OnDisk("one.m2ts", 100)]);
    }
}
