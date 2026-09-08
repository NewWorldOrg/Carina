using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Tests.Migration;

internal static class CarriedMigrationFixtures
{
    public static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Ended = new(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);

    public static readonly MigrationSourceName Source = new("the recording system being replaced");

    public static readonly ServiceKey InReach = ServiceKey.Of(32736, 1024);

    public static readonly OutputRoot Root = new("carried");

    public static IReadOnlySet<ServiceKey> Rescanned() => new HashSet<ServiceKey> { InReach };

    public static SourceRecording Recording(long id)
        => new(id, "a programme", Began, Ended, InReach, new EventId(4321));

    public static SourceRecording RecordingOfNoProgramme(long id)
        => new(id, "a programme", Began, Ended, InReach, null);

    public static SourceRecordingFile AsBroadcast(long recordingId, string path, long size)
        => new(recordingId, path, SourceFileKind.AsBroadcast, size);

    public static SourceFile OnDisk(string path, long size) => new(path, size);

    public static SourceLedger Ledger(
        IReadOnlyList<SourceRecording>? recordings = null,
        IReadOnlyList<SourceRecordingFile>? files = null)
        => new(Source, recordings ?? [], files ?? [], [], [], []);

    public static MigrationRoll Rolled(SourceLedger ledger, IReadOnlyList<SourceFile> onDisk)
        => MigrationClassifier.Over(ledger, onDisk, Rescanned());
}

internal sealed class MigrationJournal
{
    public List<string> Steps { get; } = [];
}

internal sealed class HeldMigratedRecordings(MigrationJournal journal) : IRecordingRepository
{
    public List<Recording> Written { get; } = [];

    public Exception? WhenAdding { get; set; }

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        journal.Steps.Add($"row {recording.FileName.Value}");

        if (WhenAdding is { } refused)
        {
            throw refused;
        }

        Written.Add(recording);

        return Task.CompletedTask;
    }

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Written.FirstOrDefault(recording => recording.Id.Equals(id)));

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([]);

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([]);

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ScriptedCarrier(MigrationJournal journal) : IMigrationCarrier
{
    private readonly Dictionary<string, MigrationCarry> answers = new(StringComparer.Ordinal);

    public OutputRoot Into { get; } = CarriedMigrationFixtures.Root;

    public List<string> Named { get; } = [];

    public MigrationCarry Otherwise { get; set; } = MigrationCarry.Linked();

    public MigrationRootStanding Standing { get; set; } = MigrationRootStanding.Empty;

    public Task<MigrationRootStanding> StandingAsync(CancellationToken cancellationToken)
        => Task.FromResult(Standing);

    public void Answers(string sourcePath, MigrationCarry carry) => answers[sourcePath] = carry;

    public Task<MigrationCarry> CarryAsync(
        string sourcePath,
        RecordingFileName name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        journal.Steps.Add($"link {sourcePath}");
        Named.Add(name.Value);

        return Task.FromResult(answers.GetValueOrDefault(sourcePath, Otherwise));
    }
}
