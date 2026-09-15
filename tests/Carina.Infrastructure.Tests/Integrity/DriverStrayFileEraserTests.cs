using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class DriverStrayFileEraserTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Primary = new("primary");

    private static readonly DateTime Noticed = new(2026, 8, 26, 5, 0, 0, DateTimeKind.Utc);

    private readonly string root = Directory.CreateTempSubdirectory("carina-stray-").FullName;

    private readonly ErasingDriverClient driver = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task AFileStillAsItWasFoundIsHandedToTheDriverWithExactlyWhatTheCheckKept()
    {
        Holding("nested/leftover.tmp");
        IntegrityFinding finding = Found("nested/leftover.tmp");

        StrayFileErasure erased = await Eraser().EraseAsync(finding, Cancel);

        StrayFileErasureRequest asked = Assert.Single(driver.AskedAboutStrays);
        Assert.Null(erased.Fault);
        Assert.True(erased.FileRemoved);
        Assert.Equal("primary", asked.OutputRoot);
        Assert.Equal("nested/leftover.tmp", asked.Path);
        Assert.Equal(finding.ObservedSize, asked.SizeBytes);
        Assert.Equal(finding.LastWrittenAt, asked.LastWrittenAt.UtcDateTime);
    }

    [Fact]
    public async Task TheAppNeverTakesTheFileOffTheDiskItselfAndLeavesThatToTheDriver()
    {
        string stray = Holding("leftover.tmp");

        await Eraser().EraseAsync(Found("leftover.tmp"), Cancel);

        Assert.True(File.Exists(stray));
        Assert.Single(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task AFileThatGrewSinceItWasFoundIsRefusedBeforeTheDriverIsAsked()
    {
        string stray = Holding("leftover.tmp");
        IntegrityFinding finding = Found("leftover.tmp");
        await File.AppendAllTextAsync(stray, "more");

        StrayFileErasure refused = await Eraser().EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.FileChanged, refused.Fault);
        Assert.Equal(StrayFileChange.Resized, refused.Change);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task AFileWrittenToAgainAtTheSameSizeIsRefusedBeforeTheDriverIsAsked()
    {
        string stray = Holding("leftover.tmp");
        IntegrityFinding finding = Found("leftover.tmp");
        File.SetLastWriteTimeUtc(stray, finding.LastWrittenAt!.Value.AddMinutes(1));

        StrayFileErasure refused = await Eraser().EraseAsync(finding, Cancel);

        Assert.Equal(StrayFileChange.Rewritten, refused.Change);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task AFileAlreadyGoneIsRefusedAsGone()
    {
        Holding("kept.bin");
        string stray = Holding("leftover.tmp");
        IntegrityFinding finding = Found("leftover.tmp");
        File.Delete(stray);

        StrayFileErasure refused = await Eraser().EraseAsync(finding, Cancel);

        Assert.Equal(StrayFileChange.Gone, refused.Change);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ARootHoldingNothingAtAllIsTakenForALostMountAndNothingIsAsked()
    {
        string stray = Holding("leftover.tmp");
        IntegrityFinding finding = Found("leftover.tmp");
        File.Delete(stray);

        StrayFileErasure refused = await Eraser().EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.RootOutOfReach, refused.Fault);
        Assert.Null(refused.Change);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ARootTheDriverNoLongerDeclaresIsRefusedBeforeAnythingIsAsked()
    {
        Holding("leftover.tmp");
        driver.Declaring = DriverCall<IReadOnlyList<StorageRootDto>>.Reached(
            [new StorageRootDto { Name = "bulk", Writable = true }]);

        StrayFileErasure refused = await Eraser().EraseAsync(Found("leftover.tmp"), Cancel);

        Assert.Equal(StrayErasureFault.RootOutOfReach, refused.Fault);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ARootThisProcessCannotReadIsRefusedBeforeAnythingIsAsked()
    {
        Holding("leftover.tmp");
        IntegrityFinding finding = Found("leftover.tmp");
        var unmounted = new DriverStrayFileEraser(
            driver,
            new LocalRecordingFileSurvey(new IntegritySettings(), NullLogger<LocalRecordingFileSurvey>.Instance),
            NullLogger<DriverStrayFileEraser>.Instance);

        StrayFileErasure refused = await unmounted.EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.RootOutOfReach, refused.Fault);
        Assert.Empty(driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ADeclarationThatCouldNotBeReadIsADriverThatDidNotAnswer()
    {
        Holding("leftover.tmp");
        driver.Declaring = DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable("the socket was not there");

        StrayFileErasure refused = await Eraser().EraseAsync(Found("leftover.tmp"), Cancel);

        Assert.Equal(StrayErasureFault.DriverUnreachable, refused.Fault);
        Assert.Equal("the socket was not there", refused.Note);
    }

    [Theory]
    [InlineData(SessionRefusalTitles.OutputUnavailable, StrayErasureFault.RootOutOfReach)]
    [InlineData(SessionRefusalTitles.StrayFileChanged, StrayErasureFault.FileChanged)]
    [InlineData(SessionRefusalTitles.RecordingInProgress, StrayErasureFault.BeingWritten)]
    [InlineData(SessionRefusalTitles.FileLeftBehind, StrayErasureFault.FileLeftBehind)]
    [InlineData(SessionRefusalTitles.CapabilityMissing, StrayErasureFault.DriverRefused)]
    [InlineData(SessionRefusalTitles.Rejected, StrayErasureFault.DriverRefused)]
    public async Task WhatTheDriverRefusedWithIsReadAsTheFaultItMeans(string title, StrayErasureFault fault)
    {
        Holding("leftover.tmp");
        driver.StrayAnswer = DriverCall<StrayFileErasedDto>.Refused(new DriverProblem(title, ["it said no"]));

        StrayFileErasure refused = await Eraser().EraseAsync(Found("leftover.tmp"), Cancel);

        Assert.Equal(fault, refused.Fault);
        Assert.Contains("it said no", refused.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatDidNotAnswerTheErasureIsNotADriverThatRefused()
    {
        Holding("leftover.tmp");
        driver.StrayAnswer = DriverCall<StrayFileErasedDto>.Unreachable("the socket went away");

        StrayFileErasure refused = await Eraser().EraseAsync(Found("leftover.tmp"), Cancel);

        Assert.Equal(StrayErasureFault.DriverUnreachable, refused.Fault);
    }

    [Fact]
    public async Task AFindingThatIsNotAFileNobodyOwnsIsNeverHandedOver()
    {
        Holding("one.m2ts");
        IntegrityFinding aboutARecording = IntegrityFinding.SizeDisagrees(
            IntegrityCheckId.New(),
            Primary,
            RecordingId.New(),
            new RecordingFileName("one.m2ts"),
            100,
            99,
            Noticed);
        IntegrityFinding unstamped = IntegrityFinding.NoLedgerRow(
            IntegrityCheckId.New(),
            Primary,
            "one.m2ts",
            64,
            Noticed);

        await Assert.ThrowsAsync<ArgumentException>(() => Eraser().EraseAsync(aboutARecording, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => Eraser().EraseAsync(unstamped, Cancel));
        Assert.Empty(driver.AskedAboutStrays);
    }

    private IStrayFileEraser Eraser()
        => new DriverStrayFileEraser(
            driver,
            new LocalRecordingFileSurvey(
                new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, root)] },
                NullLogger<LocalRecordingFileSurvey>.Instance),
            NullLogger<DriverStrayFileEraser>.Instance);

    private string Holding(string path, int size = 64)
    {
        string full = Path.Combine(root, path);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);

        return full;
    }

    private IntegrityFinding Found(string path)
    {
        var file = new FileInfo(Path.Combine(root, path));

        return IntegrityFinding.NoLedgerRow(
            IntegrityCheckId.New(),
            Primary,
            path,
            file.Length,
            Noticed,
            file.LastWriteTimeUtc);
    }
}
