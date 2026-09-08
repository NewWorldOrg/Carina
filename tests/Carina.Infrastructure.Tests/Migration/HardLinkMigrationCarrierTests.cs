using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class HardLinkMigrationCarrierTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Root = new("carried");

    private readonly string from = Directory.CreateTempSubdirectory("carina-migration-from").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-migration-into").FullName;

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact]
    public async Task WhatArrivesIsTheSameFileUnderANewNameAndNotACopyOfIt()
    {
        RecordingId id = RecordingId.New();
        await File.WriteAllTextAsync(Path.Combine(from, "one.m2ts"), "first", Cancel);

        MigrationCarry carried = await Carrier().CarryAsync("one.m2ts", Named(id), Cancel);

        Assert.Equal(MigrationCarryOutcome.Linked, carried.Outcome);

        await File.AppendAllTextAsync(Path.Combine(from, "one.m2ts"), " and second", Cancel);

        Assert.Equal(
            "first and second",
            await File.ReadAllTextAsync(Path.Combine(into, Named(id).Value), Cancel));
    }

    [Fact]
    public async Task AFileWhoseNameIsNotWrittenInLatinLettersIsCarriedAllTheSame()
    {
        RecordingId id = RecordingId.New();
        await File.WriteAllTextAsync(Path.Combine(from, "日曜劇場 第１話.m2ts"), "a recording", Cancel);

        MigrationCarry carried = await Carrier()
            .CarryAsync("日曜劇場 第１話.m2ts", Named(id), Cancel);

        Assert.Equal(MigrationCarryOutcome.Linked, carried.Outcome);
        Assert.Equal("a recording", await File.ReadAllTextAsync(Path.Combine(into, Named(id).Value), Cancel));
    }

    [Fact]
    public async Task TheSourceIsLeftExactlyWhereItWasAndAsItWas()
    {
        string original = Path.Combine(from, "one.m2ts");
        await File.WriteAllTextAsync(original, "the recording", Cancel);
        DateTime written = File.GetLastWriteTimeUtc(original);
        byte[] before = await File.ReadAllBytesAsync(original, Cancel);

        await Carrier().CarryAsync("one.m2ts", Named(RecordingId.New()), Cancel);

        Assert.Equal(["one.m2ts"], Directory.GetFiles(from).Select(Path.GetFileName));
        Assert.Equal(before, await File.ReadAllBytesAsync(original, Cancel));
        Assert.Equal(written, File.GetLastWriteTimeUtc(original));
    }

    [Fact]
    public async Task AFileTheLedgerNamesAndTheDiskDoesNotIsSaidToBeGone()
    {
        MigrationCarry carried = await Carrier().CarryAsync("missing.m2ts", Named(RecordingId.New()), Cancel);

        Assert.Equal(MigrationCarryOutcome.SourceGone, carried.Outcome);
    }

    [Fact]
    public async Task SomethingAlreadyUnderThatNameIsSaidToBeThereRatherThanOverwritten()
    {
        RecordingId id = RecordingId.New();
        await File.WriteAllTextAsync(Path.Combine(from, "one.m2ts"), "the recording", Cancel);
        await File.WriteAllTextAsync(Path.Combine(into, Named(id).Value), "something else", Cancel);

        MigrationCarry carried = await Carrier().CarryAsync("one.m2ts", Named(id), Cancel);

        Assert.Equal(MigrationCarryOutcome.AlreadyThere, carried.Outcome);
        Assert.Equal("something else", await File.ReadAllTextAsync(Path.Combine(into, Named(id).Value), Cancel));
    }

    [Fact]
    public async Task ANewRootThatIsNotThereIsRefusedRatherThanMade()
    {
        await File.WriteAllTextAsync(Path.Combine(from, "one.m2ts"), "the recording", Cancel);
        string absent = Path.Combine(into, "not-made-yet");

        MigrationCarry carried = await new HardLinkMigrationCarrier(from, absent, Root)
            .CarryAsync("one.m2ts", Named(RecordingId.New()), Cancel);

        Assert.Equal(MigrationCarryOutcome.Refused, carried.Outcome);
        Assert.False(Directory.Exists(absent));
    }

    [Fact]
    public async Task APathThatClimbsOutOfTheSourceDirectoryIsRefused()
    {
        MigrationCarry carried = await Carrier().CarryAsync("../one.m2ts", Named(RecordingId.New()), Cancel);

        Assert.Equal(MigrationCarryOutcome.Refused, carried.Outcome);
        Assert.Contains("output directory", carried.Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewRootOnAnotherFilesystemIsRefusedAndNothingIsCopied()
    {
        string shared = SharedMemory();
        string elsewhere = Path.Combine(shared, $"carina-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(elsewhere);

        try
        {
            RecordingId id = RecordingId.New();
            await File.WriteAllTextAsync(Path.Combine(from, "one.m2ts"), "the recording", Cancel);

            MigrationCarry carried = await new HardLinkMigrationCarrier(from, elsewhere, Root)
                .CarryAsync("one.m2ts", Named(id), Cancel);

            Assert.Equal(MigrationCarryOutcome.NotOnTheSameFilesystem, carried.Outcome);
            Assert.Contains("same filesystem", carried.Said, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(elsewhere));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public async Task ANewRootSaysWhetherItIsEmptyThereOrNotThereAtAll()
    {
        Assert.Equal(MigrationRootStanding.Empty, await Carrier().StandingAsync(Cancel));

        await File.WriteAllTextAsync(Path.Combine(into, "left-over.ts"), "from a run before", Cancel);

        Assert.Equal(MigrationRootStanding.NotEmpty, await Carrier().StandingAsync(Cancel));
        Assert.Equal(
            MigrationRootStanding.Missing,
            await new HardLinkMigrationCarrier(from, Path.Combine(into, "not-made-yet"), Root).StandingAsync(Cancel));
    }

    [Fact]
    public void EveryWayALinkCanEndIsReadIntoAnAnswerTheRecordCanName()
    {
        Assert.Equal(MigrationCarryOutcome.SourceGone, HardLinkMigrationCarrier.Read(2).Outcome);
        Assert.Equal(MigrationCarryOutcome.AlreadyThere, HardLinkMigrationCarrier.Read(17).Outcome);
        Assert.Equal(MigrationCarryOutcome.NotOnTheSameFilesystem, HardLinkMigrationCarrier.Read(18).Outcome);
        Assert.Equal(MigrationCarryOutcome.Refused, HardLinkMigrationCarrier.Read(13).Outcome);
        Assert.NotEmpty(HardLinkMigrationCarrier.Read(13).Said);
    }

    private static string SharedMemory()
    {
        Assert.True(
            Directory.Exists("/dev/shm"),
            "This test needs a second filesystem to link across, and /dev/shm is the one it looks for.");

        return "/dev/shm";
    }

    private static RecordingFileName Named(RecordingId id)
        => RecordingFileName.For(id, Carina.Contracts.RecordingFile.Extension);

    private HardLinkMigrationCarrier Carrier() => new(from, into, Root);
}
