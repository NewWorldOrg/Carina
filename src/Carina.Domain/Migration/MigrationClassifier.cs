using System.Globalization;

namespace Carina.Domain.Migration;

public static class MigrationClassifier
{
    public static MigrationVerdict OnARecording(
        SourceRecording recording,
        SourceRecordingFile? asBroadcast,
        SourceFile? onDisk)
    {
        ArgumentNullException.ThrowIfNull(recording);

        string subject = Numbered(recording.Id);

        if (recording.Service is null)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                subject,
                MigrationRefusal.Unidentifiable,
                recording.Name,
                null,
                null);
        }

        if (asBroadcast is null)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                subject,
                MigrationRefusal.FileMissing,
                recording.Name,
                null,
                null);
        }

        if (onDisk is null)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                subject,
                MigrationRefusal.FileMissing,
                recording.Name,
                asBroadcast.SizeRecorded,
                null);
        }

        if (onDisk.SizeBytes is 0)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                subject,
                MigrationRefusal.ReallyEmpty,
                recording.Name,
                asBroadcast.SizeRecorded,
                onDisk.SizeBytes);
        }

        return MigrationVerdict.Carry(
            MigrationPopulation.Recordings,
            subject,
            recording.Name,
            asBroadcast.SizeRecorded,
            onDisk.SizeBytes);
    }

    public static MigrationVerdict OnAFile(SourceFile file, SourceRecordingFile? claim)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (claim is null)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.RecordingFiles,
                file.Path,
                MigrationRefusal.Orphan,
                file.Path,
                null,
                file.SizeBytes);
        }

        if (claim.Kind is not SourceFileKind.AsBroadcast)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.RecordingFiles,
                file.Path,
                MigrationRefusal.OutOfScope,
                file.Path,
                claim.SizeRecorded,
                file.SizeBytes);
        }

        if (file.SizeBytes is 0)
        {
            return MigrationVerdict.Refuse(
                MigrationPopulation.RecordingFiles,
                file.Path,
                MigrationRefusal.ReallyEmpty,
                file.Path,
                claim.SizeRecorded,
                file.SizeBytes);
        }

        return MigrationVerdict.Carry(
            MigrationPopulation.RecordingFiles,
            file.Path,
            file.Path,
            claim.SizeRecorded,
            file.SizeBytes);
    }

    public static MigrationVerdict OnARule(SourceRule rule, IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inReach);

        string subject = Numbered(rule.Id);

        if (rule.Services.Any(service => !inReach.Contains(service)))
        {
            return Refused(MigrationPopulation.Rules, subject, MigrationRefusal.Unidentifiable, rule.Name);
        }

        if (rule.UsesRegularExpression || rule.CaseSensitive)
        {
            return Refused(MigrationPopulation.Rules, subject, MigrationRefusal.Inexpressible, rule.Name);
        }

        if (rule.RecordsAtATimeOfDay
            || rule.BoundsTheDuration
            || rule.BoundsThePeriod
            || rule.NamesItsOwnDestination
            || rule.NamesItsOwnEncodeSettings)
        {
            return Refused(MigrationPopulation.Rules, subject, MigrationRefusal.NoSuchFeature, rule.Name);
        }

        return Carried(MigrationPopulation.Rules, subject, rule.Name);
    }

    public static MigrationVerdict OnAReservation(SourceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        string subject = Numbered(reservation.Id);

        return reservation.FromARule
            ? Refused(
                MigrationPopulation.Reservations,
                subject,
                MigrationRefusal.OutOfScope,
                reservation.ProgrammeName)
            : Carried(MigrationPopulation.Reservations, subject, reservation.ProgrammeName);
    }

    public static MigrationVerdict OnAChannelDefinition(
        SourceChannelDefinition definition,
        IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inReach);

        string subject = Numbered(definition.Id);

        if (definition.Kind is SourceBroadcastKind.Sky)
        {
            return Refused(
                MigrationPopulation.ChannelDefinitions,
                subject,
                MigrationRefusal.Inexpressible,
                definition.Name);
        }

        return inReach.Contains(definition.Service)
            ? Carried(MigrationPopulation.ChannelDefinitions, subject, definition.Name)
            : Refused(
                MigrationPopulation.ChannelDefinitions,
                subject,
                MigrationRefusal.Unidentifiable,
                definition.Name);
    }

    public static MigrationRoll Over(
        SourceLedger ledger,
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<ServiceKey> inReach)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(inReach);

        Dictionary<long, SourceRecordingFile> asBroadcast = AsBroadcastByRecording(ledger.RecordingFiles);
        Dictionary<string, SourceRecordingFile> claims = ClaimsByPath(ledger.RecordingFiles);
        Dictionary<string, SourceFile> onDisk = OnDisk(files);

        List<MigrationVerdict> verdicts = [];

        foreach (SourceRecording recording in ledger.Recordings)
        {
            SourceRecordingFile? row = asBroadcast.GetValueOrDefault(recording.Id);

            verdicts.Add(OnARecording(recording, row, row is null ? null : onDisk.GetValueOrDefault(row.Path)));
        }

        foreach (SourceFile file in onDisk.Values)
        {
            verdicts.Add(OnAFile(file, claims.GetValueOrDefault(file.Path)));
        }

        foreach (SourceRule rule in ledger.Rules)
        {
            verdicts.Add(OnARule(rule, inReach));
        }

        foreach (SourceReservation reservation in ledger.Reservations)
        {
            verdicts.Add(OnAReservation(reservation));
        }

        foreach (SourceChannelDefinition definition in ledger.ChannelDefinitions)
        {
            verdicts.Add(OnAChannelDefinition(definition, inReach));
        }

        Dictionary<MigrationPopulation, int> offered = new()
        {
            [MigrationPopulation.Recordings] = ledger.Recordings.Count,
            [MigrationPopulation.RecordingFiles] = onDisk.Count,
            [MigrationPopulation.Rules] = ledger.Rules.Count,
            [MigrationPopulation.Reservations] = ledger.Reservations.Count,
            [MigrationPopulation.ChannelDefinitions] = ledger.ChannelDefinitions.Count,
        };

        return MigrationRoll.Of(offered, InAStableOrder(verdicts));
    }

    private static MigrationVerdict Carried(MigrationPopulation population, string subject, string note)
        => MigrationVerdict.Carry(population, subject, note, null, null);

    private static MigrationVerdict Refused(
        MigrationPopulation population,
        string subject,
        MigrationRefusal refusal,
        string note)
        => MigrationVerdict.Refuse(population, subject, refusal, note, null, null);

    private static Dictionary<long, SourceRecordingFile> AsBroadcastByRecording(
        IReadOnlyList<SourceRecordingFile> rows)
    {
        Dictionary<long, SourceRecordingFile> found = [];

        foreach (SourceRecordingFile row in rows)
        {
            if (row.Kind is not SourceFileKind.AsBroadcast)
            {
                continue;
            }

            if (!found.TryAdd(row.RecordingId, row))
            {
                throw new ArgumentException(
                    $"A recording was written once, so recording {row.RecordingId} of the source system "
                    + "cannot name two files as broadcast.",
                    nameof(rows));
            }
        }

        return found;
    }

    private static Dictionary<string, SourceRecordingFile> ClaimsByPath(IReadOnlyList<SourceRecordingFile> rows)
    {
        Dictionary<string, SourceRecordingFile> found = new(StringComparer.Ordinal);

        foreach (SourceRecordingFile row in rows)
        {
            if (!found.TryAdd(row.Path, row))
            {
                throw new ArgumentException(
                    $"A file answers one row, so '{row.Path}' cannot be claimed twice.",
                    nameof(rows));
            }
        }

        return found;
    }

    private static Dictionary<string, SourceFile> OnDisk(IReadOnlyList<SourceFile> files)
    {
        Dictionary<string, SourceFile> found = new(StringComparer.Ordinal);

        foreach (SourceFile file in files)
        {
            ArgumentNullException.ThrowIfNull(file);

            if (!found.TryAdd(file.Path, file))
            {
                throw new ArgumentException(
                    $"A directory holds one file per path, so '{file.Path}' cannot be listed twice.",
                    nameof(files));
            }
        }

        return found;
    }

    private static IReadOnlyList<MigrationVerdict> InAStableOrder(List<MigrationVerdict> verdicts)
        => [.. verdicts
            .OrderBy(verdict => verdict.Population)
            .ThenBy(verdict => verdict.Subject, StringComparer.Ordinal)];

    private static string Numbered(long id) => id.ToString(CultureInfo.InvariantCulture);
}
