extern alias driver;

using System.Runtime.Versioning;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using driver::Carina.Driver.Configuration;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class DriverMovedByItsLedgerTests
{
    [Fact]
    public async Task ARewrittenLedgerAndARestartMoveWhereTheDriverListensAndWritesWithNothingRebuilt()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        SyntheticDriverHost driver = feature.Driver;
        string firstSocket = driver.SocketPath;
        string firstRecordings = driver.RecordingsDirectory;
        string movedSocket = driver.Beside("moved.sock");
        string movedRecordings = driver.Beside("moved-recordings");
        DriverConfiguration moved = driver.Configuration with
        {
            SocketPath = movedSocket,
            OutputRoots = [new OutputRootSettings(SyntheticDriverHost.RootName, movedRecordings)],
        };

        await driver.PutDownAsync();
        await feature.App.UntilConnectionIs("notConnected");

        driver.WriteLedger(moved);

        using var refusal = new StringWriter();

        Assert.Equal(DriverStartup.ConfigurationExitCode, await driver.RaiseFromTheLedgerAsync(refusal));
        Assert.Contains($"outputRoots[0].path: '{movedRecordings}' does not exist.", refusal.ToString(), StringComparison.Ordinal);
        Assert.Contains(driver.LedgerPath, refusal.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(movedSocket), "A driver whose ledger was refused bound its socket anyway.");
        Assert.False(File.Exists(firstSocket), "A driver whose ledger was refused is still answering where it used to.");

        await feature.App.UntilConnectionIs("notConnected");

        Directory.CreateDirectory(movedRecordings);

        using var acceptance = new StringWriter();

        Assert.Equal(0, await driver.RaiseFromTheLedgerAsync(acceptance));
        Assert.Empty(acceptance.ToString());
        Assert.Equal(movedSocket, driver.SocketPath);

        await feature.StartAppAsync();

        RecordingRun begun = await feature.App.TickAsync();
        RecordingId started = Assert.Single(begun.Started);
        string file = feature.FileOf(started);

        Assert.Equal(movedRecordings, Path.GetDirectoryName(file));

        await feature.UntilTheFileGrowsPast(file, 0);

        Assert.Empty(Directory.EnumerateFileSystemEntries(firstRecordings));

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun overRun = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(overRun.Stopped));

        await feature.UntilTheRecordingSessionIsStopped();

        Assert.Equal([Path.GetFileName(file)], feature.FilesWritten());
    }
}
