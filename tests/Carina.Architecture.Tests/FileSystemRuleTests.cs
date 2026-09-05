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
        "/Carina.Domain/Streaming/LiveCaptions.cs .CopyTo(",
        "/Carina.Domain/Streaming/LiveFrame.cs .CopyTo(",
        "/Carina.Driver/Configuration/AtomicFile.cs File.Delete",
        "/Carina.Driver/Configuration/AtomicFile.cs File.Move",
        "/Carina.Driver/Configuration/AtomicFile.cs FileMode.",
        "/Carina.Driver/Configuration/AtomicFile.cs newFileStream",
        "/Carina.Driver/Configuration/DriverConfigurationReader.cs File.Create",
        "/Carina.Driver/Configuration/DriverConfigurationReader.cs File.Delete",
        "/Carina.Driver/Descrambling/AribB25Library.cs NativeLibrary",
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
        "/Carina.Infrastructure/Encodings/EncodeArtefactPlacer.cs File.Move",
        "/Carina.Infrastructure/Encodings/EncodeScratchCleaner.cs File.Delete",
        "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.CreateDirectory",
        "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.Delete",
        "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.Move",
        "/Carina.Infrastructure/Integrity/LocalRecordingFileSurvey.cs .Replace(",
        "/Carina.Infrastructure/Machines/AnotherProgramme.cs Process.Start",
        "/Carina.Infrastructure/Machines/AnotherProgramme.cs ProcessStartInfo",
        "/Carina.Infrastructure/Machines/MachineCapabilityReader.cs File.Open",
        "/Carina.Infrastructure/Machines/MachineCapabilityReader.cs FileMode.",
        "/Carina.Infrastructure/Programmes/ProgrammeSearchQuery.cs .Replace(",
        "/Carina.Infrastructure/Recordings/DriverRecordingFileEraser.cs File.Delete",
        "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs Process.Start",
        "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs ProcessStartInfo",
        "/Carina.Infrastructure/Streaming/NutFrames.cs .CopyTo(",
        "/Carina.Infrastructure/Streaming/TranscoderProcess.cs Process.Start",
        "/Carina.Infrastructure/Streaming/TranscoderProcess.cs ProcessStartInfo",
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
    public void TheOnlyPlacesThatStartAProgrammeOfTheirOwnAreTheOnesThatAskFfmpegSomething()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Machines/AnotherProgramme.cs Process.Start",
                "/Carina.Infrastructure/Machines/AnotherProgramme.cs ProcessStartInfo",
                "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs Process.Start",
                "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs ProcessStartInfo",
                "/Carina.Infrastructure/Streaming/TranscoderProcess.cs Process.Start",
                "/Carina.Infrastructure/Streaming/TranscoderProcess.cs ProcessStartInfo",
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs Process.Start",
                "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs ProcessStartInfo",
            ],
            Inventory.Where(entry => entry.Contains("Process", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void WhatReadsAStreamsAttributesReadsAndWritesNoFileOfItsOwn()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs Process.Start",
                "/Carina.Infrastructure/Streaming/FfprobeStreamAttributeReader.cs ProcessStartInfo",
            ],
            Inventory
                .Where(entry => entry.Contains("FfprobeStreamAttributeReader", StringComparison.Ordinal))
                .ToArray());
    }

    private static bool OpensSomething(string entry)
        => !entry.Contains("Process", StringComparison.Ordinal)
           && !entry.Contains(".CopyTo(", StringComparison.Ordinal);

    [Fact]
    public void TheLivePathOpensNoFileOfItsOwn()
    {
        Assert.DoesNotContain(
            Inventory.Where(entry => entry.Contains("/Streaming/", StringComparison.Ordinal)),
            OpensSomething);
    }

    [Fact]
    public void TheOnlyFileOpenedToAskAboutTheCardIsTheRenderNode()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Machines/MachineCapabilityReader.cs File.Open",
                "/Carina.Infrastructure/Machines/MachineCapabilityReader.cs FileMode.",
            ],
            Inventory
                .Where(entry => entry.Contains("/Machines/", StringComparison.Ordinal))
                .Where(OpensSomething)
                .ToArray());

        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Machines",
            "MachineCapabilityReader.cs"));

        Assert.Contains(
            "File.Open(settings.RenderNode, FileMode.Open, FileAccess.ReadWrite)",
            source,
            StringComparison.Ordinal);
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

    [Fact(DisplayName = "BR-ED2-009/010: the encode feature moves a work file once, deletes only by the ledger, and probes a rename with an empty directory")]
    public void TheEncodeFeatureMovesOnceDeletesByTheLedgerAndProbesARenameWithAnEmptyDirectory()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Encodings/EncodeArtefactPlacer.cs File.Move",
                "/Carina.Infrastructure/Encodings/EncodeScratchCleaner.cs File.Delete",
                "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.CreateDirectory",
                "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.Delete",
                "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.Move",
            ],
            Inventory.Where(entry => entry.Contains("/Encodings/", StringComparison.Ordinal)).ToArray());

        string placer = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Encodings",
            "EncodeArtefactPlacer.cs"));

        Assert.Contains("File.Move(work, artefact, overwrite: false)", placer, StringComparison.Ordinal);
        Assert.DoesNotContain("overwrite: true", placer, StringComparison.Ordinal);
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
