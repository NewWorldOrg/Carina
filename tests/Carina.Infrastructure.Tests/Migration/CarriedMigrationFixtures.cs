using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Tests.Rules;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Migration;

internal static class CarriedMigrationFixtures
{
    public static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Ended = new(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);

    public static readonly MigrationSourceName Source = new("the recording system being replaced");

    public static readonly ServiceKey InReach = ServiceKey.Of(32736, 1024);

    public static readonly ServiceKey Elsewhere = ServiceKey.Of(32737, 2048);

    public static readonly OutputRoot Root = new("carried");

    public static IReadOnlyList<RescannedService> Rescanned() => [new RescannedService(InReach, "a station")];

    public static SourceRecording Recording(long id)
        => new(id, "a programme", Began, Ended, InReach, new EventId(4321));

    public static SourceRecording RecordingOfNoProgramme(long id)
        => new(id, "a programme", Began, Ended, InReach, null);

    public static SourceRecordingFile AsBroadcast(long recordingId, string path, long size)
        => new(recordingId, path, SourceFileKind.AsBroadcast, size);

    public static SourceFile OnDisk(string path, long size) => new(path, size);

    public static SourceLedger Ledger(
        IReadOnlyList<SourceRecording>? recordings = null,
        IReadOnlyList<SourceRecordingFile>? files = null,
        IReadOnlyList<SourceRule>? rules = null,
        IReadOnlyList<SourceChannelDefinition>? channels = null)
        => new(Source, recordings ?? [], files ?? [], rules ?? [], [], channels ?? []);

    public static SourceRule Rule(
        long id,
        string keyword = "hill",
        bool enabled = true,
        int days = SourceWeek.EveryDay)
        => new(
            id,
            keyword,
            enabled,
            new SourceRuleTerms(
                keyword,
                string.Empty,
                SourceRuleFields.Title | SourceRuleFields.Summary,
                SourceRuleFields.Title | SourceRuleFields.Summary,
                [],
                [],
                [],
                days),
            SourceRuleReach.Plain);

    public static SourceChannelDefinition Channel(long id, ServiceKey service, string physicalChannel = "21")
        => new(id, "an old name", SourceBroadcastKind.Terrestrial, service, physicalChannel);

    public static MigrationRoll Rolled(SourceLedger ledger, IReadOnlyList<SourceFile> onDisk)
        => MigrationClassifier.Over(ledger, onDisk, RescannedService.InReach(Rescanned()));
}

internal sealed class MigrationBench
{
    public MigrationBench(TimeProvider clock)
    {
        Clock = clock;
        Profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            CarriedMigrationFixtures.Began);
        Destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Encodes"),
            new OutputRoot("encodes"),
            Profile.Id,
            CarriedMigrationFixtures.Began);
        Profiles.Profiles.Add(Profile);
        Destinations.Destinations.Add(Destination);
    }

    public MigrationJournal Journal { get; } = new();

    public TimeProvider Clock { get; }

    public EncodeProfile Profile { get; }

    public EncodeDestination Destination { get; }

    public HeldRules Rules { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldEncodeDestinations Destinations { get; } = new();

    public MigrationCarriage Carriage(IMigrationCarrier carrier, IRecordingRepository recordings)
        => new(carrier, recordings, Rules, Jobs, Destinations, Profiles, Clock);
}

internal sealed class MigrationJournal
{
    public List<string> Steps { get; } = [];
}

internal sealed class HeldMigratedRecordings(MigrationJournal journal) : IRecordingRepository
{
    public List<Recording> Written { get; } = [];

    public Exception? WhenAdding { get; set; }

    public int WrittenBeforeItRefuses { get; set; }

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        journal.Steps.Add($"row {recording.FileName.Value}");

        if (WhenAdding is { } refused && Written.Count >= WrittenBeforeItRefuses)
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
