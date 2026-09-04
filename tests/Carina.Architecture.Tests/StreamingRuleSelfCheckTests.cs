namespace Carina.Architecture.Tests;

public sealed class StreamingRuleSelfCheckTests
{
    private const string Wire = "Carina.Api/Live/LiveWire.cs";

    private const string Supply = "Carina.Infrastructure/Streaming/LiveSupply.cs";

    public static TheoryData<string, string> EveryMarkOfAParserOnItsOwn => new()
    {
        { "TsPacketReader reader = new(bytes);", "TsPacketReader" },
        { "foreach (TsPacket packet in packets)", "TsPacket" },
        { "ContinuityCounterTracker counting = new();", "ContinuityCounterTracker" },
        { "if (bytes[at] != 0x47) continue;", "0x47" },
        { "for (int at = 0; at + 188 <= read; at += 188)", "188" },
        { "byte[] payload = new byte[184];", "184" },
        { "int pid = ((bytes[1] << 8) | bytes[2]) & 0x1FFF;", "0x1FFF" },
        { "int counter = bytes[3] & 0x0F;", "0x0F" },
        { "int expected = ContinuityCounterOf(bytes);", "ContinuityCounterOf" },
        { "int scrambling = (bytes[3] >> 6) & 3; if (ScramblingIsSet(bytes)) scrambled++;", "ScramblingIsSet" },
        { "// counts transport_scrambling_control per packet", "transport_scrambling_control" },
    };

    public static TheoryData<string> EveryWayOfCountingThatWalksStraightPast =>
    [
        "for (int at = 0; at + 4 + 180 + 4 <= read; at += 4 + 180 + 4) { }",
        "if (bytes[at] != 71) continue;",
        "int pid = ((bytes[1] << 8) | bytes[2]) & 0x1fff;",
        "int counter = bytes[3] & 15;",
        "long dropped = CountGaps(bytes);",
        "arguments.Add(\"-map\"); arguments.Add(\"0\"); arguments.Add(\"-f\"); arguments.Add(\"null\");",
    ];

    public static TheoryData<string, string> EveryWayOfWritingWhatIsNotStreamings => new()
    {
        { "await recordings.AddAsync(recording, cancellationToken);", ".AddAsync(" },
        { "await recordings.SaveAsync(recording, cancellationToken);", ".SaveAsync(" },
        { "await recordings.HaltAsync(id, reason, at, cancellationToken);", ".HaltAsync(" },
        { "await recordings.DiscardAsync(id, cancellationToken);", ".DiscardAsync(" },
        { "await programmes.ForgetAsync(gone, cancellationToken);", ".ForgetAsync(" },
        { "await programmes.ForgetEverythingAsync(cancellationToken);", ".ForgetEverythingAsync(" },
        { "await services.RemoveAsync(networkId, serviceId, cancellationToken);", ".RemoveAsync(" },
        { "await driver.EraseRecordingAsync(id, root, cancellationToken);", ".EraseRecordingAsync(" },
        { "await driver.ReplaceTunerLedgerAsync(tuners, cancellationToken);", ".ReplaceTunerLedgerAsync(" },
        { "await driver.ToggleTunerAsync(deviceId, true, cancellationToken);", ".ToggleTunerAsync(" },
        { "await context.SaveChangesAsync(cancellationToken);", ".SaveChangesAsync(" },
        { "public sealed class LiveLedger(CarinaDbContext context)", "CarinaDbContext" },
        { "private readonly DbSet<Recording> rows;", "DbSet<" },
        { "await context.Recordings.ExecuteDeleteAsync(cancellationToken);", "ExecuteDeleteAsync" },
        { "await context.Recordings.ExecuteUpdateAsync(set => set.SetProperty(r => r.Note, note));", "ExecuteUpdateAsync" },
        { "await context.Database.ExecuteSqlAsync($\"UPDATE recording SET note = {note}\");", "ExecuteSqlAsync" },
        { "context.Entry(recording).Property(r => r.Outcome).CurrentValue = outcome;", ".Entry(" },
        { "const string Sql = \"DELETE FROM recording WHERE id = @id\";", "DELETEFROM" },
        { "const string Sql = \"insert into programme (id) values (@id)\";", "insertinto" },
        { "public sealed class LiveLedger(IRecordingRepository recordings)", "IRecordingRepository" },
        { "public sealed class LiveLedger(TunerLedgerService tuners)", "TunerLedgerService" },
        { "public sealed class LiveLedger(ProgrammeWriter guide)", "ProgrammeWriter" },
        { "public sealed class LiveLedger(IRecordingFileEraser eraser)", "IRecordingFileEraser" },
        { "File.Delete(path);", "File.Delete" },
        { "File.Move(path, elsewhere);", "File.Move" },
        { "await File.WriteAllBytesAsync(path, bytes, cancellationToken);", "File.WriteAllBytesAsync" },
        { "Directory.Delete(root, recursive: true);", "Directory.Delete" },
        { "using FileStream sink = File.Open(path, FileMode.Create);", "FileMode.Create" },
        { "using FileStream sink = new FileStream(path, FileMode.Open);", "newFileStream" },
        { "await using StreamWriter log = new StreamWriter(path);", "newStreamWriter" },
    };

    public static TheoryData<string> EveryWayOfReadingThatIsLeftAlone =>
    [
        "Recording? recording = await recordings.FindAsync(id, cancellationToken);",
        "IReadOnlyList<Programme> onAir = await programmes.ListForServicesAsync(services, window, cancellationToken);",
        "IReadOnlyList<BroadcastService> channels = await services.ListAsync(cancellationToken);",
        "DriverCall<IReadOnlyList<SessionSnapshot>> sessions = await driver.GetActiveSessionsAsync(cancellationToken);",
        "DriverCall<SessionSnapshot> started = await driver.StartSessionAsync(request, cancellationToken);",
        "DriverCall<SessionSnapshot> stopped = await driver.StopSessionAsync(sessionId, reason, cancellationToken);",
        "using FileStream reading = File.OpenRead(path);",
        "if (!File.Exists(path)) return null;",
        "using FileStream node = File.Open(renderNode, FileMode.Open, FileAccess.ReadWrite);",
        "DropTimeline drops = recording.Drops; long lost = recording.Counters.Dropped;",
        "frames.Add(frame); viewers.Remove(viewer);",
    ];

    public static TheoryData<string, string> EveryWayOfLayingOutARefusalByHand => new()
    {
        { "payload[0] = (byte)LiveRefusal.WouldNotTune;", "(byte)LiveRefusal." },
        { "byte[] said = [(byte) LiveRefusal.NoTunerFree, 0, 0, 0, 0];", "(byte)LiveRefusal." },
        { "payload[1] = (byte)TuneFailureKind.NoLock;", "(byte)TuneFailureKind." },
        { "payload[1] = (byte)LiveTunerHolder.ARecording;", "(byte)LiveTunerHolder." },
        { "byte[] payload = new byte[LiveRefusalReport.PayloadLength];", "LiveRefusalReport.PayloadLength" },
        { "if (said.Payload.Length == LiveRefusalReport.PayloadLength) { }", "LiveRefusalReport.PayloadLength" },
    };

    public static TheoryData<string> EveryWayOfLayingOutARefusalThatWalksStraightPast =>
    [
        "await socket.SendAsync(report.ToPayload(), cancellationToken);",
        "payload[0] = (byte)Refusal; payload[1] = detail.Said;",
        "byte reason = (byte)refusal; byte said = (byte)kind;",
        "byte[] payload = new byte[5];",
        "byte[] payload = new byte[PayloadLength];",
        "payload[0] = (byte)LiveChannel.Control; payload[1] = (byte)LiveControl.Ping;",
    ];

    public static TheoryData<string> EveryWayOfWritingThatWalksStraightPast =>
    [
        "await recordings.PersistAsync(recording, cancellationToken);",
        "await recordings.WriteAsync(recording, cancellationToken);",
        "await commit(recording, cancellationToken);",
        "await context.Database.RunAsync(Sql);",
        "const string Sql = \"UPDATE recording\" + \" SET note = @note\";",
    ];

    [Theory]
    [MemberData(nameof(EveryMarkOfAParserOnItsOwn))]
    public void DetectsOneMarkOfAParserInsideTheFeature(string source, string mark)
    {
        using SourceTree tree = new();
        tree.Write(Supply, source);

        Assert.Equal([$"/{Supply} {mark}"], StreamingRules.WhatTakesTheStreamApartInsideTheFeature(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfCountingThatWalksStraightPast))]
    public void CannotSeeACounterThatSpellsItsNumbersDifferentlyOrCountsSomewhereElse(string source)
    {
        using SourceTree tree = new();
        tree.Write(Supply, source);

        Assert.Empty(StreamingRules.WhatTakesTheStreamApartInsideTheFeature(tree.Root));
    }

    [Fact]
    public void LeavesTheDriversOwnCounterAlone()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Driver/Sessions/TunerSession.cs",
            "TsPacketReader reader = new(); ContinuityCounterTracker counting = new(); if (b != 0x47) skip(188);");

        Assert.Empty(StreamingRules.WhatTakesTheStreamApartInsideTheFeature(tree.Root));
    }

    [Fact]
    public void LeavesAFeatureThatReadsWhatWasCountedAlone()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Api/Playback/PlayDelivery.cs",
            "DropTimeline drops = recording.Drops; bool measured = recording.CcMeasured; long lost = recording.CcDroppedPackets;");

        Assert.Empty(StreamingRules.WhatTakesTheStreamApartInsideTheFeature(tree.Root));
    }

    [Fact]
    public void DetectsASecondFileOpeningTheDriversStream()
    {
        using SourceTree tree = new();
        tree.Write(Supply, "DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(id, DriverEndpoints.ViewerSubscriber, abort);");
        tree.Write(
            "Carina.Infrastructure/Streaming/LiveDropCounter.cs",
            "DriverCall<Stream> second = await driver.OpenSessionStreamAsync(id, DriverEndpoints.ViewerSubscriber, abort);");

        Assert.Equal(
            ["/Carina.Infrastructure/Streaming/LiveDropCounter.cs", $"/{Supply}"],
            StreamingRules.FilesOpeningTheDriversStream(tree.Root));
    }

    [Fact]
    public void DetectsOneFileOpeningTheDriversStreamTwice()
    {
        string source =
            "DriverCall<Stream> picture = await driver.OpenSessionStreamAsync(id, DriverEndpoints.ViewerSubscriber, abort);"
            + "DriverCall<Stream> counted = await driver.OpenSessionStreamAsync(id, DriverEndpoints.ViewerSubscriber, abort);";

        Assert.Equal(2, StreamingRules.TimesTheDriversStreamIsOpenedIn(source));
    }

    [Fact]
    public void LeavesOneFileOpeningTheDriversStreamOnceAsTheViewerAlone()
    {
        using SourceTree tree = new();
        tree.Write(Supply, "DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(id, DriverEndpoints.ViewerSubscriber, abort);");

        Assert.Equal([$"/{Supply}"], StreamingRules.FilesOpeningTheDriversStream(tree.Root));
        Assert.Equal(1, StreamingRules.TimesTheDriversStreamIsOpenedIn(File.ReadAllText(Path.Combine(tree.Root, Supply))));
        Assert.Empty(StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(tree.Root));
    }

    [Theory]
    [InlineData("DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(id, DriverEndpoints.SurveySubscriber, abort);", "SurveySubscriber")]
    [InlineData("DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(id, DriverEndpoints.PiggybackSubscriber, abort);", "PiggybackSubscriber")]
    [InlineData("string path = DriverEndpoints.SessionStream(id);", "SessionStream(")]
    [InlineData("string path = $\"/sessions/{id}\" + \"/stream\";", "\"/stream\"")]
    [InlineData("string path = $\"/sessions/{id}/stream?as=viewer\";", "/stream?")]
    public void DetectsAnotherSeatBeingAskedForOrThePathSpelledByHand(string source, string found)
    {
        using SourceTree tree = new();
        tree.Write(Supply, source);

        Assert.Equal([$"/{Supply} {found}"], StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(tree.Root));
    }

    [Fact]
    public void LeavesTheHarvestersOutsideTheFeatureTheirOwnSeats()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Infrastructure/Collection/StreamVisitor.cs",
            "DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(sessionId, DriverEndpoints.SurveySubscriber, abort);");

        Assert.Empty(StreamingRules.FilesOpeningTheDriversStream(tree.Root));
        Assert.Empty(StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(tree.Root));
    }

    [Fact]
    public void CannotSeeASeatSpelledAsALiteralOrAStreamOpenedThroughADelegate()
    {
        using SourceTree tree = new();
        tree.Write(Supply, "DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(id, \"survey\", abort);");
        tree.Write(
            "Carina.Infrastructure/Streaming/LiveDropCounter.cs",
            "DriverCall<Stream> second = await open(id, DriverEndpoints.ViewerSubscriber, abort);");

        Assert.Equal([$"/{Supply}"], StreamingRules.FilesOpeningTheDriversStream(tree.Root));
        Assert.Empty(StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWritingWhatIsNotStreamings))]
    public void DetectsThisWayOfWritingWhatIsNotStreamings(string source, string way)
    {
        using SourceTree tree = new();
        tree.Write(Wire, source);

        Assert.Contains($"/{Wire} {way}", StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root), StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryWayOfReadingThatIsLeftAlone))]
    public void LeavesThisWayOfReadingAlone(string source)
    {
        using SourceTree tree = new();
        tree.Write(Wire, source);

        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWritingThatWalksStraightPast))]
    public void CannotSeeAWriteSpelledWithAVerbItDoesNotKnowOrHiddenBehindADelegate(string source)
    {
        using SourceTree tree = new();
        tree.Write(Wire, source);

        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
    }

    [Fact]
    public void JudgesAFileOutsideTheFoldersByTheNamespacesItNames()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Api/Services/LiveChannelService.cs",
            "using Carina.Domain.Streaming; await services.SaveAsync(service, cancellationToken);");
        tree.Write(
            "Carina.Api/Services/TunerLedgerService.cs",
            "using Carina.Domain.Channels; await driver.ReplaceTunerLedgerAsync(tuners, cancellationToken);");

        Assert.Equal(
            ["/Carina.Api/Services/LiveChannelService.cs .SaveAsync("],
            StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
    }

    [Fact]
    public void LeavesTheCompositionRootAloneBecauseItNamesEverything()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
            "using Carina.Domain.Streaming; services.AddScoped<IRecordingRepository, RecordingRepository>(); services.AddDbContext<CarinaDbContext>();");
        tree.Write(
            "Carina.Api/Program.cs",
            "using Carina.Api.Live; app.MapGet(LiveWire.Path, LiveWire.Invoke); builder.Services.AddSingleton<TunerLedgerService>();");

        Assert.Empty(StreamingRules.FilesInTheFeature(tree.Root));
        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
    }

    [Fact]
    public void CannotSeeAWriterThatNeitherSitsInTheFoldersNorNamesTheNamespaces()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Infrastructure/Sessions/LiveLedger.cs",
            "public sealed class LiveLedger(CarinaDbContext context) { public Task NoteAsync() => context.SaveChangesAsync(); }");

        Assert.Empty(StreamingRules.FilesInTheFeature(tree.Root));
        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
    }

    [Fact]
    public void TheEdgeIdentityRuleReadsAFileOfTheStreamingFeatureLikeAnyOther()
    {
        using SourceTree tree = new();
        tree.Write(Wire, "string? who = context.Request.Headers[\"X-Forwarded-User\"];");
        tree.Write("Carina.Api/Playback/PlayDelivery.cs", "string? who = SessionClaims.SubjectOf(context.User);");

        Assert.Equal(
            [Wire],
            SourceScan.FilesMentioning(tree.Root, [.. AuthenticationBypasses.EdgeIdentityHeaders]));
    }

    [Fact]
    public void TheEdgeIdentityRuleCannotSeeTheHeaderPutTogetherOutOfPieces()
    {
        using SourceTree tree = new();
        tree.Write(Wire, "string? who = context.Request.Headers[\"X-Forwarded-\" + \"User\"];");

        Assert.Empty(SourceScan.FilesMentioning(tree.Root, [.. AuthenticationBypasses.EdgeIdentityHeaders]));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfLayingOutARefusalByHand))]
    public void DetectsARefusalLaidOutSomewhereOtherThanTheReportAndItsDetail(string source, string way)
    {
        using SourceTree tree = new();
        tree.Write(Wire, source);

        Assert.Equal([$"/{Wire} {way}"], StreamingRules.WhatLaysOutARefusalOutsideItsOwnFiles(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfLayingOutARefusalThatWalksStraightPast))]
    public void CannotSeeARefusalLaidOutWithoutNamingTheEnumsOrTheLength(string source)
    {
        using SourceTree tree = new();
        tree.Write(Wire, source);

        Assert.Empty(StreamingRules.WhatLaysOutARefusalOutsideItsOwnFiles(tree.Root));
    }

    [Fact]
    public void LeavesTheReportAndItsDetailAloneBecauseLayingItOutIsWhatTheyAreFor()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Domain/Streaming/LiveRefusalReport.cs",
            "byte[] payload = new byte[LiveRefusalReport.PayloadLength]; payload[0] = (byte)LiveRefusal.NoTunerFree;");
        tree.Write(
            "Carina.Domain/Streaming/LiveRefusalDetail.cs",
            "public byte Said => (byte)TuneFailureKind.NoLock;");

        Assert.Empty(StreamingRules.WhatLaysOutARefusalOutsideItsOwnFiles(tree.Root));
    }

    [Fact]
    public void LeavesARefusalLaidOutOutsideTheStreamingFeatureAloneBecauseThisRuleOnlyReadsTheFeature()
    {
        using SourceTree tree = new();
        tree.Write(
            "Carina.Infrastructure/Recordings/RecordingRound.cs",
            "byte reason = (byte)LiveRefusal.NoTunerFree;");

        Assert.Empty(StreamingRules.WhatLaysOutARefusalOutsideItsOwnFiles(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using SourceTree tree = new();

        Assert.Empty(StreamingRules.FilesInTheFeature(tree.Root));
        Assert.Empty(StreamingRules.WhatTakesTheStreamApartInsideTheFeature(tree.Root));
        Assert.Empty(StreamingRules.FilesOpeningTheDriversStream(tree.Root));
        Assert.Empty(StreamingRules.WhatAsksForAnotherSeatInsideTheFeature(tree.Root));
        Assert.Empty(StreamingRules.WhatWritesWhatIsNotItsOwnInsideTheFeature(tree.Root));
        Assert.Empty(StreamingRules.WhatLaysOutARefusalOutsideItsOwnFiles(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-streaming-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }

    [Fact]
    public void DetectsAPromiseWaitedOnWithNoDeadlineAndLetsOneCarryingADeadlinePass()
    {
        Assert.Equal(
            ["awaitraised.Task"],
            StreamingRules.WhatWaitsWithoutADeadlineIn("LiveJoin? answer = await raised.Task;"));
        Assert.Equal(
            [".Task.WaitAsync(cancellationToken)"],
            StreamingRules.WhatWaitsWithoutADeadlineIn("LiveJoin? answer = await raised.Task.WaitAsync(cancellationToken);"));
        Assert.Empty(StreamingRules.WhatWaitsWithoutADeadlineIn(
            "LiveJoin? answer = await raised.Task.WaitAsync(settings.LongestRaise, clock, cancellationToken);"));
        Assert.Empty(StreamingRules.WhatWaitsWithoutADeadlineIn("await Task.WhenAny(carried, stopped.Task);"));
    }

    [Fact]
    public void ThisRuleReadsSourceTextAndAWaitSpelledAnotherWayWalksStraightPast()
    {
        Assert.Empty(StreamingRules.WhatWaitsWithoutADeadlineIn("Task<LiveJoin?> answering = raised.Task; await answering;"));
        Assert.Empty(StreamingRules.WhatWaitsWithoutADeadlineIn("LiveJoin? answer = raised.Task.GetAwaiter().GetResult();"));
        Assert.Empty(StreamingRules.WhatWaitsWithoutADeadlineIn("raised.Task.Wait();"));
    }
}
