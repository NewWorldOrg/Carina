using Carina.Domain.Migration;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Migration;

internal static class MigrationFixtures
{
    public static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Ended = new(2026, 9, 8, 3, 4, 0, DateTimeKind.Utc);

    public static readonly MigrationRunId Run = new(new Guid("00000001-0000-0000-0000-000000000001"));

    public static readonly MigrationSourceName Source = new("the recording system being replaced");

    public static readonly ServiceKey InReach = ServiceKey.Of(32736, 1024);

    public static readonly ServiceKey Elsewhere = ServiceKey.Of(32737, 2048);

    public static readonly EventId Programme = new(4321);

    public static IReadOnlySet<ServiceKey> Rescanned(params ServiceKey[] services) => services.ToHashSet();

    public static SourceRecording Recording(long id, ServiceKey? service) =>
        new(id, "a programme", Began, Ended, service, Programme);

    public static SourceRecording RecordingOfNoProgramme(long id) =>
        new(id, "a programme", Began, Ended, InReach, null);

    public static SourceRecording Recording(long id) => Recording(id, InReach);

    public static SourceRecordingFile AsBroadcast(long recordingId, string path, long sizeRecorded) =>
        new(recordingId, path, SourceFileKind.AsBroadcast, sizeRecorded);

    public static SourceRecordingFile Encoded(long recordingId, string path, long sizeRecorded) =>
        new(recordingId, path, SourceFileKind.Encoded, sizeRecorded);

    public static SourceFile OnDisk(string path, long sizeBytes) => new(path, sizeBytes);

    public static SourceRule Rule(long id) =>
        new(id, "a rule", true, [], false, false, false, false, false, false, false);

    public static SourceRule RuleOver(long id, params ServiceKey[] services) =>
        new(id, "a rule", true, services, false, false, false, false, false, false, false);

    public static SourceReservation Reservation(long id, bool fromARule) => new(id, "a programme", fromARule);

    public static SourceChannelDefinition Channel(long id, SourceBroadcastKind kind, ServiceKey service) =>
        new(id, "a station", kind, service, true);

    public static SourceLedger Ledger(
        IReadOnlyList<SourceRecording>? recordings = null,
        IReadOnlyList<SourceRecordingFile>? files = null,
        IReadOnlyList<SourceRule>? rules = null,
        IReadOnlyList<SourceReservation>? reservations = null,
        IReadOnlyList<SourceChannelDefinition>? channels = null) =>
        new(Source, recordings ?? [], files ?? [], rules ?? [], reservations ?? [], channels ?? []);

    public static MigrationRoll Roll(
        IReadOnlyDictionary<MigrationPopulation, int> offered,
        params MigrationVerdict[] verdicts) => MigrationRoll.Of(offered, verdicts);

    public static Dictionary<MigrationPopulation, int> Nothing()
        => MigrationPopulations.Counted.ToDictionary(population => population, _ => 0);

    public static MigrationVerdict Carried(MigrationPopulation population, string subject)
        => MigrationVerdict.Carry(population, subject, "a programme", null, null);

    public static MigrationVerdict Refused(
        MigrationPopulation population,
        string subject,
        MigrationRefusal refusal)
        => MigrationVerdict.Refuse(population, subject, refusal, "a programme", null, null);

    public static MigrationReport Report(MigrationRoll roll)
        => MigrationCensus.Taken(Run, Source, MigrationPass.Rehearsal, roll, Began, Ended);

    public static MigrationRun Ran() =>
        MigrationRun.Rehydrate(Run, Source, MigrationPass.Rehearsal, Began, Ended);
}
