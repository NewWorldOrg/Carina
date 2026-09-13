namespace Carina.Architecture.Tests;

public sealed class RecordingFenceRuleSelfCheckTests
{
    private const string SetsTheClaimThroughAnEntry = """
        using Microsoft.EntityFrameworkCore;
        internal sealed class ReservationTouch(CarinaDbContext context)
        {
            public void Claim(Reservation reservation, DateTime at)
                => context.Entry(reservation).Property("started_at").CurrentValue = at;
        }
        """;

    private const string ReadsTheClaimThroughAnEntry = """
        using Microsoft.EntityFrameworkCore;
        internal sealed class ReservationTouch(CarinaDbContext context)
        {
            public object? Claim(Reservation reservation)
                => context.Entry(reservation).Property("started_at").CurrentValue;
        }
        """;

    private const string SetsTheOutcomeThroughTheCurrentValues = """
        using Microsoft.EntityFrameworkCore;
        internal sealed class ReservationTouch(CarinaDbContext context)
        {
            public void Finish(Reservation reservation)
                => context.Entry(reservation).CurrentValues["recording_outcome"] = "Complete";
        }
        """;

    private const string SetsTheClaimThroughAnUntypedSetProperty = """
        using Microsoft.EntityFrameworkCore;
        internal sealed class ReservationSweep(CarinaDbContext context)
        {
            public Task ClaimAsync(DateTime at)
                => context.Set<Reservation>().ExecuteUpdateAsync(
                    setting => setting.SetProperty(row => EF.Property<DateTime?>(row, "started_at"), at));
        }
        """;

    private const string NamesTheClaimInAnInsert = """
        internal static class ReservationBackfill
        {
            public const string Sql =
                "INSERT INTO reservation (id, network_id, service_id, started_at) VALUES (@id, @nid, @sid, @at)";
        }
        """;

    private const string ReadsABroadcastTable = """
        using Carina.Domain.Recordings;
        internal sealed class RecordingWindowFollower
        {
            public void Saw(DescribedEvent announced) => window.Extend(announced.EndsAt);
        }
        """;

    private const string FollowsTheWatcherOnly = """
        using Carina.Domain.Recordings;
        internal sealed class RecordingWindowFollower
        {
            public void Saw(PresentChange change) => window.Extend(change.EndsAt);
        }
        """;

    private const string OffersADeletion = """
        using Microsoft.AspNetCore.Mvc;
        namespace Carina.Api.Controllers.Recordings;
        [Route("api/recordings/{id}/files")]
        public sealed class DropRecordingFileAction : ControllerBase
        {
            [HttpDelete]
            public IActionResult Invoke(string id) => Ok();
        }
        """;

    private const string ErasesALedgerRow = """
        using Microsoft.EntityFrameworkCore;
        internal sealed class RecordingSweep(CarinaDbContext context)
        {
            public Task<int> DropAsync(RecordingId id, CancellationToken cancellationToken)
                => context.Set<Recording>()
                    .Where(recording => recording.Id == id)
                    .ExecuteDeleteAsync(cancellationToken);
        }
        """;

    private const string AsksTheLibraryToErase = """
        internal sealed class LibraryTidy(IRecordingLibraryRepository library)
        {
            public Task<int> DropAsync(RecordingId id, CancellationToken cancellationToken)
                => library.DeleteAsync(id, cancellationToken);
        }
        """;

    private const string ReadOnlyPortThatCanWrite = """
        namespace Carina.Domain.Programmes;

        public interface IAnnouncedProgrammes
        {
            Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken);

            Task AddAsync(Programme programme, CancellationToken cancellationToken);
        }

        public interface IProgrammeRepository : IAnnouncedProgrammes
        {
            Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken);
        }
        """;

    private const string ReadOnlyPortThatOnlyReads = """
        namespace Carina.Domain.Programmes;

        public interface IAnnouncedProgrammes
        {
            Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken);
        }

        public interface IProgrammeRepository : IAnnouncedProgrammes
        {
            Task AddAsync(Programme programme, CancellationToken cancellationToken);

            Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken);
        }
        """;

    [Fact]
    public void DetectsAClaimWrittenThroughAnEntryOutsideTheTwoThatMay()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Persistence/Repositories/ReservationTouch.cs", SetsTheClaimThroughAnEntry);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/ReservationTouch.cs"],
            RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void LeavesAClaimThatIsOnlyReadThroughAnEntry()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Persistence/Repositories/ReservationTouch.cs", ReadsTheClaimThroughAnEntry);

        Assert.Empty(RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void DetectsAnOutcomeWrittenThroughTheCurrentValues()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Persistence/Repositories/ReservationTouch.cs",
            SetsTheOutcomeThroughTheCurrentValues);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/ReservationTouch.cs"],
            RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void DetectsAClaimWrittenThroughASetPropertyThatNamesTheColumnAsText()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Persistence/Repositories/ReservationSweep.cs",
            SetsTheClaimThroughAnUntypedSetProperty);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/ReservationSweep.cs"],
            RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void DetectsAClaimCarriedInOnAnInsert()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Persistence/Repositories/ReservationBackfill.cs", NamesTheClaimInAnInsert);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/ReservationBackfill.cs"],
            RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void LeavesTheCarriageThatRehydratesRowsAlone()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Migration/CarriedRecording.cs", NamesTheClaimInAnInsert);
        tree.Write("Carina.Infrastructure/Recordings/RecordingLanding.cs", SetsTheClaimThroughAnEntry);
        tree.Write(
            "Carina.Infrastructure/Persistence/Repositories/ReservationRecordingContract.cs",
            SetsTheOutcomeThroughTheCurrentValues);

        Assert.Empty(RecordingFenceRules.WritersOfWhatRecordingOwnsThroughThePropertyBag(tree.Root));
    }

    [Fact]
    public void DetectsARecordingFileThatReadsABroadcastTable()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingWindowFollower.cs", ReadsABroadcastTable);

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingWindowFollower.cs"],
            RecordingFenceRules.BroadcastTableReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void LeavesARecordingThatOnlyFollowsWhatTheWatcherSays()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingWindowFollower.cs", FollowsTheWatcherOnly);

        Assert.Empty(RecordingFenceRules.BroadcastTableReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void LeavesTheGuideReadingItsOwnTables()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Collection/StreamHarvest.cs", "using Carina.Broadcast.Tables;");

        Assert.Empty(RecordingFenceRules.BroadcastTableReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void DetectsAWriteMemberSlippingOntoThePortTheRoundHolds()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Programmes/IProgrammeRepository.cs", ReadOnlyPortThatCanWrite);

        Assert.Equal(["AddAsync"], RecordingFenceRules.WriteMembersOnThePortTheRoundHolds(tree.Root));
        Assert.Equal(["ForgetAsync"], RecordingFenceRules.WriteMembersOnThePortCollectionHolds(tree.Root));
    }

    [Fact]
    public void LeavesAReadOnlyPortThatOnlyReads()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Programmes/IProgrammeRepository.cs", ReadOnlyPortThatOnlyReads);

        Assert.Empty(RecordingFenceRules.WriteMembersOnThePortTheRoundHolds(tree.Root));
        Assert.Equal(["AddAsync", "ForgetAsync"], RecordingFenceRules.WriteMembersOnThePortCollectionHolds(tree.Root));
    }

    [Fact]
    public void DetectsASecondDeletionOfferedByTheRecordingFeature()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Controllers/Recordings/DropRecordingFileAction.cs", OffersADeletion);

        Assert.Equal(
            ["/Carina.Api/Controllers/Recordings/DropRecordingFileAction.cs"],
            RecordingFenceRules.DeletionsOfferedByTheRecordingFeature(tree.Root));
        Assert.Equal(
            ["api/recordings/{id}/files"],
            RecordingFenceRules.RoutesThatThrowARecordingAway(tree.Root));
    }

    [Fact]
    public void LeavesTheOneRouteTheLibraryOwns()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Api/Controllers/Recordings/DeleteRecordingAction.cs",
            OffersADeletion.Replace("{id}/files", "{id}", StringComparison.Ordinal));

        Assert.Empty(RecordingFenceRules.DeletionsOfferedByTheRecordingFeature(tree.Root));
        Assert.Equal(["api/recordings/{id}"], RecordingFenceRules.RoutesThatThrowARecordingAway(tree.Root));
    }

    [Fact]
    public void DetectsAThirdPlaceThatErasesALedgerRow()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Persistence/Repositories/RecordingSweep.cs", ErasesALedgerRow);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/RecordingSweep.cs"],
            RecordingFenceRules.WhatErasesARecordingLedgerRow(tree.Root));
    }

    [Fact]
    public void DetectsTheUnaskedErasureBeingWiredUp()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Services/LibraryTidy.cs", AsksTheLibraryToErase);

        Assert.Equal(
            ["/Carina.Api/Services/LibraryTidy.cs"],
            RecordingFenceRules.WhatAsksTheLibraryToEraseALedgerRow(tree.Root));
    }

    [Fact]
    public void LeavesAFileThatOnlyNamesTheLibraryPort()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
            "services.AddScoped<IRecordingLibraryRepository, RecordingLibraryRepository>();");

        Assert.Empty(RecordingFenceRules.WhatAsksTheLibraryToEraseALedgerRow(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-recording-fences-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
