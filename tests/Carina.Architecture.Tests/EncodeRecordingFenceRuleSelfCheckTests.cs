namespace Carina.Architecture.Tests;

public sealed class EncodeRecordingFenceRuleSelfCheckTests
{
    public static TheoryData<string, string> EachWayOfWritingTheRecording() =>
        new()
        {
            { "telling the recording how it ended", "private void End(Recording recording) => recording.Settle(RecordingOutcome.Failed, 0, At);" },
            { "an outcome spelled at the call, under another name", "private void End(Recording source) => source.Settle(RecordingOutcome.Truncated, 12, At);" },
            { "a reason added under another name", "private void Why(Recording source) => source.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, At));" },
            { "a reason added to the recording", "private void Why(Recording recording, OutcomeDetail detail) => recording.Note(detail);" },
            { "moving the recording along", "private void Stop(Recording recorded) => recorded.Abort(At);" },
            { "the end the recording has moved", "private void Later(Recording recording) => recording.Extend(At);" },
            { "writing it back through the port", "private Task Back(IRecordingRepository ledger, Recording found) => ledger.SaveAsync(found, default);" },
            { "adding one through the port", "private Task Anew(IRecordingRepository ledger, Recording found) => ledger.AddAsync(found, default);" },
            { "raw SQL", "private const string Sql = \"UPDATE recording SET recording_outcome = 'Failed'\";" },
            { "the store behind the port", "private Task Mark(CarinaDbContext context) => context.Set<Recording>().Where(row => row.Outcome == null).ExecuteUpdateAsync(set => set, default);" },
        };

    [Theory]
    [MemberData(nameof(EachWayOfWritingTheRecording))]
    public void EveryOrdinaryWayOfWritingTheRecordingIsCaughtWhereverInTheFeatureTheFileSits(string how, string writes)
    {
        foreach (string relative in new[]
                 {
                     "Carina.Infrastructure/Encodings/Runner.cs",
                     "Carina.Api/Controllers/Encoding/CallOffAction.cs",
                     "Carina.Api/Services/EncodeCallOff.cs",
                 })
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-recording-");

            try
            {
                Write(directory, relative, Source(writes));

                Assert.NotEmpty(EncodeRecordingFenceRules.WhatWritesTheRecordingItWasMadeFrom(directory.FullName));
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(how));
    }

    public static TheoryData<string> WhatTheFeatureDoesThatIsNotAWriteToTheRecording() =>
        new()
        {
            "private void Swept(EncodeScratchFile scratch) => scratch.Settle(EncodeScratchFate.Removed, At);",
            "private void Failed(EncodeJob job) => job.Fail(EncodeFailure.TimedOut, string.Empty, At);",
            "private Task<Recording?> Found(IRecordingRepository recordings, RecordingId id) => recordings.FindAsync(id, default);",
            "private static string Read(Recording recording) => recording.FileName.Value + recording.OutcomeDetail.Count;",
            "private const string Sql = \"SELECT file_size_observed FROM recording WHERE id = @id\";",
        };

    [Theory]
    [MemberData(nameof(WhatTheFeatureDoesThatIsNotAWriteToTheRecording))]
    public void ReadingTheRecordingAndEndingTheJobWalkPastThisRule(string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-reads-");

        try
        {
            Write(directory, "Carina.Infrastructure/Encodings/Runner.cs", Source(writes));

            Assert.Empty(EncodeRecordingFenceRules.WhatWritesTheRecordingItWasMadeFrom(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AWriteOutsideTheFeatureIsNotThisRulesToReport()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-outside-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Recordings/Settler.cs",
                Source("private void End(Recording recording) => recording.Settle(RecordingOutcome.Complete, 12, At);"));

            Assert.Empty(EncodeRecordingFenceRules.WhatWritesTheRecordingItWasMadeFrom(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AReportNamesTheFileAndTheCallItSaw()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-report-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Encodings/Runner.cs",
                Source("private Task Back(IRecordingRepository ledger, Recording found) => ledger . SaveAsync (found, default);"));

            Assert.Equal(
                ["/Carina.Infrastructure/Encodings/Runner.cs ledger.SaveAsync("],
                EncodeRecordingFenceRules.WhatWritesTheRecordingItWasMadeFrom(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AWriteBehindAHelperWhoseNamesSayNothingOfARecordingWalksStraightPast()
        => Assert.Empty(EncodeRecordingFenceRules.Writes(
            "private static void Close(dynamic it, dynamic how, long size) => it.Settle(how, size, At);"));

    private static string Source(params string[] lines)
        => "namespace Carina.Infrastructure.Encodings;\n\npublic sealed class Runner\n{\n    "
            + string.Join("\n    ", lines)
            + "\n}\n";

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
