namespace Carina.Architecture.Tests;

public sealed class FileSystemRuleTests
{
    private static readonly string[] Inventory =
    [
        "/Carina.Api/Controllers/Auth/LogOutAction.cs .Delete(",
        "/Carina.Api/Events/ProgrammeFeedStream.cs newStreamWriter",
        "/Carina.Broadcast/Descriptors/ExtendedEventDescription.cs .CopyTo(",
        "/Carina.Broadcast/Sections/SectionAssembler.cs .CopyTo(",
        "/Carina.Domain/Programmes/ProgrammeSearchText.cs .Replace(",
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
        "/Carina.Driver/Recording/RecordingEraser.cs File.Delete",
        "/Carina.Driver/Recording/RecordingWriter.cs FileMode.",
        "/Carina.Driver/Recording/RecordingWriter.cs newFileStream",
        "/Carina.Driver/Tuning/Dvb/DvbSystemCalls.cs LibraryImport",
        "/Carina.Driver/Tuning/Dvb/DvbTunerDetector.cs .Replace(",
        "/Carina.Driver/Tuning/TunerLedgerStore.cs .Replace(",
        "/Carina.Infrastructure/Auth/SigningKeys.cs .Create()",
        "/Carina.Infrastructure/Collection/StreamHarvest.cs .CopyTo(",
        "/Carina.Infrastructure/Integrity/LocalRecordingFileSurvey.cs .Replace(",
        "/Carina.Infrastructure/Programmes/ProgrammeSearchQuery.cs .Replace(",
        "/Carina.Infrastructure/Recordings/DriverRecordingFileEraser.cs File.Delete",
        "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Directory.CreateDirectory",
        "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Process.Start",
        "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs ProcessStartInfo",
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
    public void TheOneBareCreateLeftIsAKeyFactoryAndNotAFile()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Auth/SigningKeys.cs .Create()"],
            Inventory.Where(entry => entry.EndsWith(".Create()", StringComparison.Ordinal)).ToArray());

        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Auth",
            "SigningKeys.cs"));

        Assert.Equal([".Create()"], FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(source));
        Assert.Contains("using RSA rsa = RSA.Create();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlyPlaceThatStartsAProgrammeOfItsOwnIsTheOneThatDrawsThumbnails()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Process.Start",
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs ProcessStartInfo",
            ],
            Inventory.Where(entry => entry.Contains("Process", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void WhatDrawsThumbnailsMakesTheRoomForThemAndOpensNoFileOfTheRecordingItself()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Directory.CreateDirectory",
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Process.Start",
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs ProcessStartInfo",
            ],
            Inventory.Where(entry => entry.Contains("/Thumbnails/", StringComparison.Ordinal)).ToArray());
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
