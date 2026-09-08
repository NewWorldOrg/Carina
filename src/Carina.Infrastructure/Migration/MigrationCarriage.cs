using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationCarriage(
    IMigrationCarrier carrier,
    IRecordingRepository recordings,
    TimeProvider clock)
{
    public async Task<MigrationRoll> CarryAsync(
        SourceLedger ledger,
        MigrationRoll intended,
        MigrationPass pass,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(intended);

        Dictionary<string, SourceRecording> known = ledger.Recordings.ToDictionary(
            recording => recording.Id.ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        Dictionary<long, SourceRecordingFile> named = ledger.RecordingFiles
            .Where(file => file.Kind is SourceFileKind.AsBroadcast)
            .ToDictionary(file => file.RecordingId);

        if (pass is MigrationPass.ForReal)
        {
            MigrationRootStanding standing = await carrier.StandingAsync(cancellationToken);

            if (standing is not MigrationRootStanding.Empty)
            {
                throw new MigrationCarryRefusedException(
                    $"The new root '{carrier.Into.Value}' is {Said(standing)}, and a run does not add to what is "
                    + "already there. Empty the ledger, delete the new root, make it again and start over.");
            }
        }

        List<MigrationVerdict> settled = [];

        foreach (MigrationVerdict verdict in intended.Verdicts)
        {
            if (verdict.Population is not MigrationPopulation.Recordings || !verdict.Carried)
            {
                settled.Add(verdict);

                continue;
            }

            SourceRecording source = known.TryGetValue(verdict.Subject, out SourceRecording? held)
                ? held
                : throw new MigrationUnclassifiedException(
                    $"Recording '{verdict.Subject}' was judged, and the source ledger holds no such row.");

            SourceRecordingFile file = named.TryGetValue(source.Id, out SourceRecordingFile? row)
                ? row
                : throw new MigrationUnclassifiedException(
                    $"Recording '{verdict.Subject}' is to be carried, and the source ledger names no file for it.");

            if (source.Service is not { } service || source.Programme is not { } programme)
            {
                settled.Add(Instead(verdict, MigrationRefusal.Unidentifiable));

                continue;
            }

            if (source.EndAt <= source.StartAt)
            {
                settled.Add(Instead(verdict, MigrationRefusal.Inexpressible));

                continue;
            }

            RecordingId id = RecordingId.New();
            RecordingFileName name = RecordingFileName.For(id, RecordingFile.Extension);

            if (pass is MigrationPass.Rehearsal)
            {
                settled.Add(verdict);

                continue;
            }

            MigrationCarry carried = await carrier.CarryAsync(file.Path, name, cancellationToken);

            if (carried.Outcome is MigrationCarryOutcome.SourceGone)
            {
                settled.Add(Instead(verdict, MigrationRefusal.FileMissing));

                continue;
            }

            if (!carried.Carried)
            {
                throw new MigrationCarryRefusedException($"'{file.Path}' was not carried: {carried.Said}");
            }

            await recordings.AddAsync(
                Held(source, service, programme, id, name, verdict.Observed ?? 0),
                cancellationToken);

            settled.Add(verdict);
        }

        return MigrationRoll.Of(
            intended.Populations.ToDictionary(population => population, intended.OfferedIn),
            settled);
    }

    private static string Said(MigrationRootStanding standing)
        => standing is MigrationRootStanding.Missing ? "not there" : "not empty";

    private static MigrationVerdict Instead(MigrationVerdict verdict, MigrationRefusal refusal)
        => MigrationVerdict.Refuse(
            verdict.Population,
            verdict.Subject,
            refusal,
            verdict.Note,
            verdict.Claimed,
            verdict.Observed);

    private Recording Held(
        SourceRecording source,
        ServiceKey service,
        EventId programme,
        RecordingId id,
        RecordingFileName name,
        long observed)
        => Recording.Rehydrate(
            id,
            null,
            new ProgrammeRef(service.Network, service.Service, programme, source.StartAt),
            carrier.Into,
            name,
            observed,
            clock.GetUtcNow().UtcDateTime,
            source.StartAt,
            source.EndAt,
            source.EndAt,
            (long)(source.EndAt - source.StartAt).TotalMilliseconds,
            0,
            [],
            source.StartAt,
            source.EndAt,
            RecordingOutcome.Complete,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Pending,
            ProgrammeSnapshot.Of(source.Name, string.Empty, [], [], source.StartAt),
            null,
            BroadcastGroupRole.Standalone);
}
