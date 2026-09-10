using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Infrastructure.Migration;

public sealed record MigrationCarried(MigrationRoll Roll, MigrationAftermath Aftermath);

public sealed class MigrationCarriage(
    IMigrationCarrier carrier,
    IRecordingRepository recordings,
    IRuleRepository rules,
    IEncodeJobRepository jobs,
    IEncodeDestinationRepository destinations,
    IEncodeProfileRepository profiles,
    TimeProvider clock)
{
    public async Task<MigrationCarried> CarryAsync(
        MigrationRunId runId,
        SourceLedger ledger,
        IReadOnlyList<RescannedService> rescanned,
        MigrationRoll intended,
        MigrationPass pass,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(rescanned);
        ArgumentNullException.ThrowIfNull(intended);

        IReadOnlySet<ServiceKey> inReach = RescannedService.InReach(rescanned);

        Dictionary<string, SourceRecording> known = ledger.Recordings.ToDictionary(
            recording => recording.Id.ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        Dictionary<string, SourceRule> asked = ledger.Rules.ToDictionary(
            rule => rule.Id.ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        Dictionary<long, SourceRecordingFile> named = ledger.RecordingFiles
            .Where(file => file.Kind is SourceFileKind.AsBroadcast)
            .ToDictionary(file => file.RecordingId);

        EncodeUnasked queueing = await QueueingAsync(cancellationToken);

        if (pass is MigrationPass.ForReal)
        {
            MigrationRootStanding standing = await carrier.StandingAsync(cancellationToken);

            if (standing is not MigrationRootStanding.Empty)
            {
                throw new MigrationCarryRefusedException(
                    $"The new root '{carrier.Into.Value}' is {Said(standing)}, and a run does not add to what is "
                    + "already there. Empty the ledger, delete the new root, make it again and start over.");
            }

            if (!queueing.IsSettled)
            {
                throw new MigrationCarryRefusedException(
                    "What is carried over is encoded, and that needs exactly one destination that is still "
                    + "offered, whose default profile is still offered. Define one and start over.");
            }
        }

        List<MigrationVerdict> settled = [];
        List<MigrationRuleProposal> meant = [];

        foreach (MigrationVerdict verdict in intended.Verdicts)
        {
            if (!verdict.Carried)
            {
                settled.Add(verdict);

                continue;
            }

            if (verdict.Population is MigrationPopulation.Rules)
            {
                settled.Add(verdict);
                meant.Add(await MadeAsync(runId, verdict, asked, inReach, pass, cancellationToken));

                continue;
            }

            if (verdict.Population is not MigrationPopulation.Recordings)
            {
                settled.Add(verdict);

                continue;
            }

            settled.Add(await CarriedAsync(verdict, known, named, queueing, pass, cancellationToken));
        }

        return new MigrationCarried(
            MigrationRoll.Of(
                intended.Populations.ToDictionary(population => population, intended.OfferedIn),
                settled),
            new MigrationAftermath(
                MigrationChannelProposal.Over(
                    runId,
                    ledger.ChannelDefinitions,
                    RescannedService.NamedBy(rescanned)),
                meant,
                ledger.Rules.Count,
                MigrationTextLoss.RowsPastRestoring(ledger),
                MigrationRuleConversion.RulesNarrowedByDay(ledger, inReach)));
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

    private async Task<EncodeUnasked> QueueingAsync(CancellationToken cancellationToken)
        => EncodeUnasked.Of(
            await destinations.ListAsync(cancellationToken),
            await profiles.ListAsync(cancellationToken));

    private async Task<MigrationRuleProposal> MadeAsync(
        MigrationRunId runId,
        MigrationVerdict verdict,
        IReadOnlyDictionary<string, SourceRule> asked,
        IReadOnlySet<ServiceKey> inReach,
        MigrationPass pass,
        CancellationToken cancellationToken)
    {
        SourceRule source = asked.TryGetValue(verdict.Subject, out SourceRule? held)
            ? held
            : throw new MigrationUnclassifiedException(
                $"Rule '{verdict.Subject}' was judged, and the source ledger holds no such row.");

        MigrationRuleConversion conversion = MigrationRuleConversion.Of(source, inReach);

        if (conversion.Query is not { } query)
        {
            throw new MigrationUnclassifiedException(
                $"Rule '{verdict.Subject}' is to be carried and cannot be written down as one, which leaves it "
                + "with no judgement at all.");
        }

        if (pass is MigrationPass.Rehearsal)
        {
            return MigrationRuleProposal.Rehydrate(runId, source.Id, null, source.Enabled);
        }

        Rule made = Rule.Draft(
            RuleId.New(),
            conversion.Name,
            query,
            Priority.Default,
            false,
            Margin.None,
            Margin.None,
            clock.GetUtcNow().UtcDateTime);

        await rules.AddAsync(made, cancellationToken);

        return MigrationRuleProposal.Rehydrate(runId, source.Id, made.Id, source.Enabled);
    }

    private async Task<MigrationVerdict> CarriedAsync(
        MigrationVerdict verdict,
        IReadOnlyDictionary<string, SourceRecording> known,
        IReadOnlyDictionary<long, SourceRecordingFile> named,
        EncodeUnasked queueing,
        MigrationPass pass,
        CancellationToken cancellationToken)
    {
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
            return Instead(verdict, MigrationRefusal.Unidentifiable);
        }

        if (source.EndAt <= source.StartAt)
        {
            return Instead(verdict, MigrationRefusal.Inexpressible);
        }

        RecordingId id = RecordingId.New();
        RecordingFileName name = RecordingFileName.For(id, RecordingFile.Extension);

        if (pass is MigrationPass.Rehearsal)
        {
            return verdict;
        }

        MigrationCarry carried = await carrier.CarryAsync(file.Path, name, cancellationToken);

        if (carried.Outcome is MigrationCarryOutcome.SourceGone)
        {
            return Instead(verdict, MigrationRefusal.FileMissing);
        }

        if (!carried.Carried)
        {
            throw new MigrationCarryRefusedException($"'{file.Path}' was not carried: {carried.Said}");
        }

        await recordings.AddAsync(
            Held(source, service, programme, id, name, verdict.Observed ?? 0),
            cancellationToken);

        if (queueing is not { Destination: { } destination, Profile: { } profile })
        {
            throw new MigrationCarryRefusedException(
                "What is carried over is encoded, and nothing says where to put it.");
        }

        await jobs.AddAsync(
            EncodeJob.Queue(
                EncodeJobId.New(),
                id,
                profile.Id,
                destination.Id,
                destination.OutputRoot,
                clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        return verdict;
    }

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
