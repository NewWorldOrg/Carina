using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Thumbnails;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeMountCheckTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Encodes = new("encodes");

    [Fact(DisplayName = "A-エンコード-024: with no working directory named, work is written beside the artefact and there is nothing to compare")]
    public async Task WithNoWorkingDirectoryNamedThereIsNothingToCompare()
    {
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldBeARename, string.Empty));
        var log = new HeardOf<EncodeMountCheck>();

        await Check(null, shelf.Root, probe, log).StartAsync(Cancel);

        Assert.Contains(log.Said, line => line.Contains("names nothing", StringComparison.Ordinal));
        Assert.Contains(log.Said, line => line.Contains("Encode root encodes", StringComparison.Ordinal) && line.Contains("by rename", StringComparison.Ordinal));
        Assert.Equal([(shelf.Root, shelf.Root)], probe.Asked);
        Assert.Empty(log.Warnings);
    }

    [Fact(DisplayName = "A-エンコード-024: a working directory on the same mount as every held root lets the process start")]
    public async Task AWorkingDirectoryOnTheSameMountAsEveryRootLetsTheProcessStart()
    {
        using var workshop = new TempTree();
        using var shelf = new TempTree();
        var log = new HeardOf<EncodeMountCheck>();

        await Check(workshop.Root, shelf.Root, new DirectoryRenameProbe(), log).StartAsync(Cancel);

        Assert.Contains(log.Said, line => line.Contains("takes a work file by rename", StringComparison.Ordinal));
        Assert.Empty(log.Warnings);
        Assert.Empty(workshop.Snapshot());
        Assert.Empty(shelf.Snapshot());
    }

    [Fact(DisplayName = "A-エンコード-024: a working directory on another mount than a held root stops the process, naming the setting")]
    public async Task AWorkingDirectoryOnAnotherMountThanARootStopsTheProcess()
    {
        using var workshop = new TempTree();
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldCrossAMount, "Invalid cross-device link"));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Check(workshop.Root, shelf.Root, probe, new HeardOf<EncodeMountCheck>()).StartAsync(Cancel));

        Assert.Contains(EncodeMountCheck.Setting, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("encodes", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("different mount", refusal.Message, StringComparison.Ordinal);
        Assert.Equal([(workshop.Root, shelf.Root)], probe.Asked);
    }

    [Fact(DisplayName = "A-エンコード-024: a working directory that is not there stops the process before anything is probed")]
    public async Task AWorkingDirectoryThatIsNotThereStopsTheProcess()
    {
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldBeARename, string.Empty));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Check(shelf.Under("nowhere"), shelf.Root, probe, new HeardOf<EncodeMountCheck>()).StartAsync(Cancel));

        Assert.Contains(EncodeMountCheck.Setting, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(probe.Asked);
    }

    [Fact(DisplayName = "A-エンコード-024: a working directory this process cannot write stops the process")]
    public async Task AWorkingDirectoryThisProcessCannotWriteStopsTheProcess()
    {
        using var workshop = new TempTree();
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.CannotWriteFrom, "Permission denied"));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Check(workshop.Root, shelf.Root, probe, new HeardOf<EncodeMountCheck>()).StartAsync(Cancel));

        Assert.Contains("Permission denied", refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "A-エンコード-024: a held root this process cannot write is reported and does not stop the process")]
    public async Task ARootThisProcessCannotWriteIsReportedAndDoesNotStopTheProcess()
    {
        using var workshop = new TempTree();
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.CannotWriteTo, "Read-only file system"));
        var log = new HeardOf<EncodeMountCheck>();

        await Check(workshop.Root, shelf.Root, probe, log).StartAsync(Cancel);

        string warning = Assert.Single(log.Warnings);
        Assert.Contains("Encode root encodes", warning, StringComparison.Ordinal);
        Assert.Contains("Read-only file system", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHeldRootThatIsNotThereIsReportedWhetherOrNotAWorkingDirectoryIsNamed()
    {
        using var shelf = new TempTree();
        var log = new HeardOf<EncodeMountCheck>();

        await Check(null, shelf.Under("nowhere"), new DirectoryRenameProbe(), log).StartAsync(Cancel);

        Assert.Single(log.Warnings);
        Assert.Empty(shelf.Snapshot());
    }

    [Fact(DisplayName = "BR-EV-001: a process that holds no root says nothing can be encoded, and probes nothing")]
    public async Task AProcessThatHoldsNoRootSaysNothingCanBeEncoded()
    {
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldBeARename, string.Empty));
        var log = new HeardOf<EncodeMountCheck>();

        await new EncodeMountCheck(new EncodeSettings(), probe, log).StartAsync(Cancel);

        string warning = Assert.Single(log.Warnings);
        Assert.Contains(EncodeMountCheck.RootSetting, warning, StringComparison.Ordinal);
        Assert.Contains("nothing can be encoded", warning, StringComparison.Ordinal);
        Assert.Empty(probe.Asked);
    }

    [Fact(DisplayName = "BR-EV-001: the roots the recordings are read from are not probed, because nothing is written into them")]
    public async Task TheRootsTheRecordingsAreReadFromAreNotProbed()
    {
        using var shelf = new TempTree();
        var probe = new ScriptedProbe(new RenameVerdict(RenameStanding.WouldBeARename, string.Empty));

        await Check(null, shelf.Root, probe, new HeardOf<EncodeMountCheck>()).StartAsync(Cancel);

        Assert.Equal([(shelf.Root, shelf.Root)], probe.Asked);
    }

    private static EncodeMountCheck Check(string? workedIn, string shelf, IRenameProbe probe, HeardOf<EncodeMountCheck> log)
        => new(
            new EncodeSettings { WorkedIn = workedIn, OutputRoots = [new StorageRootPath(Encodes, shelf)] },
            probe,
            log);
}
