namespace Carina.Architecture.Tests;

public sealed class FileSystemRuleTests
{
    private static readonly string[] Inventory =
    [
        "/Carina.Api/Common/ProgrammeIdText.cs .Create(",
        "/Carina.Api/Controllers/Auth/LogOutAction.cs .Delete(",
        "/Carina.Api/Events/ProgrammeFeedStream.cs newStreamWriter",
        "/Carina.Api/Services/ProgrammeGuideService.cs .Create(",
        "/Carina.Broadcast/Descriptors/ExtendedEventDescription.cs .CopyTo(",
        "/Carina.Broadcast/Sections/SectionAssembler.cs .CopyTo(",
        "/Carina.Domain/Programmes/BulkCursor.cs .Create(",
        "/Carina.Domain/Recordings/DiskPrecheckVerdict.cs .Create(",
        "/Carina.Domain/Recordings/RecordingVerdict.cs .Create(",
        "/Carina.Driver/Configuration/AtomicFile.cs File.Delete",
        "/Carina.Driver/Configuration/AtomicFile.cs File.Move",
        "/Carina.Driver/Configuration/AtomicFile.cs FileMode.",
        "/Carina.Driver/Configuration/AtomicFile.cs newFileStream",
        "/Carina.Driver/Configuration/DriverConfigurationReader.cs File.Create",
        "/Carina.Driver/Configuration/DriverConfigurationReader.cs File.Delete",
        "/Carina.Driver/Ipc/DriverSocket.cs File.Delete",
        "/Carina.Driver/Ipc/DriverSocket.cs File.SetUnixFileMode",
        "/Carina.Driver/Ipc/StorageViews.cs File.Delete",
        "/Carina.Driver/Ipc/StorageViews.cs FileMode.",
        "/Carina.Driver/Ipc/UnixFile.cs LibraryImport",
        "/Carina.Driver/Program.cs .Create(",
        "/Carina.Driver/Recording/RecordingWriter.cs FileMode.",
        "/Carina.Driver/Recording/RecordingWriter.cs newFileStream",
        "/Carina.Driver/Sessions/TunerSessionManager.cs .Create(",
        "/Carina.Driver/Tuning/Dvb/DvbSystemCalls.cs LibraryImport",
        "/Carina.Driver/Tuning/Dvb/DvbTunerDetector.cs .Replace(",
        "/Carina.Driver/Tuning/TunerLedgerStore.cs .Replace(",
        "/Carina.Infrastructure/Auth/SigningKeys.cs .Create(",
        "/Carina.Infrastructure/Collection/StreamHarvest.cs .CopyTo(",
        "/Carina.Infrastructure/Driver/DriverIpcClient.cs .Create(",
        "/Carina.Infrastructure/Integrity/LocalRecordingFileSurvey.cs .Replace(",
    ];

    [Fact]
    public void EveryWayThisRepositoryCouldChangeWhatIsOnDiskIsWrittenDownHere()
    {
        Assert.Equal(Inventory, FileSystemRules.WhatCouldChangeWhatIsOnDisk(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOnlyProcessThatWritesRecordingsIsTheOneThatOwnsTheHardware()
    {
        Assert.Equal(
            [
                "/Carina.Driver/Configuration/AtomicFile.cs newFileStream",
                "/Carina.Driver/Recording/RecordingWriter.cs newFileStream",
            ],
            Inventory.Where(entry => entry.EndsWith("newFileStream", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void NothingThatChecksTheLedgerAgainstTheFilesOpensAFileForWriting()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/LocalRecordingFileSurvey.cs .Replace("],
            Inventory.Where(entry => entry.Contains("/Integrity/", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void TheOneEntryTheLedgerCheckHasIsTextAndNotAFile()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Integrity",
            "LocalRecordingFileSurvey.cs"));

        Assert.Equal([".Replace("], FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(source));
        Assert.Contains("Path.GetRelativePath(root, entry).Replace(", source, StringComparison.Ordinal);
    }
}
