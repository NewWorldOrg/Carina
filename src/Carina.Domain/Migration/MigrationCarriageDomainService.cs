using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Rules;

using Puppis.Contracts;

namespace Carina.Domain.Migration;

/// <summary>
/// Carries what a run judged to be carried into this system. A recording is linked into the new root before its row is
/// written, arrives recorded in full and is queued for encoding with the one destination and its default profile; a
/// rule arrives turned off. A run for real stops before it carries anything when <see cref="MigrationPreconditions"/>
/// says so, and a rehearsal carries nothing and writes nothing.
/// </summary>
public sealed class MigrationCarriageDomainService(
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

        IReadOnlyDictionary<long, SourceRecordingFile> named = MigrationClassifier.AsBroadcastByRecording(
            ledger.RecordingFiles);

        EncodeUnasked queueing = await QueueingAsync(cancellationToken);

        MigrationPreconditions found = new(
            await carrier.StandingAsync(cancellationToken),
            queueing,
            await carrier.WouldCarryAsync(cancellationToken));

        if (found.Hindering(pass) is { } hindrance)
        {
            throw new MigrationCarryRefusedException(Said(hindrance, found));
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

        SourceRule[] rulesCarried =
        [
            .. settled
                .Where(verdict => verdict.Carried && verdict.Population is MigrationPopulation.Rules)
                .Select(verdict => asked[verdict.Subject]),
        ];

        IEnumerable<string> recordingsCarried = settled
            .Where(verdict => verdict.Carried && verdict.Population is MigrationPopulation.Recordings)
            .Select(verdict => known[verdict.Subject].Name);

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
                rulesCarried.Length,
                MigrationTextLoss.RowsPastRestoring(
                    recordingsCarried.Concat(rulesCarried.Select(rule => rule.Name))),
                MigrationRuleConversion.RulesNarrowedByDay(ledger, inReach),
                MigrationRuleConversion.RulesWithMarkWordsReplaced(ledger, inReach)),
            found.NewRoot,
            queueing.Standing,
            found.Carrying);
    }

    private string Said(MigrationHindrance hindrance, MigrationPreconditions found)
        => hindrance switch
        {
            MigrationHindrance.TheNewRootIsNotEmptyOrNotThere =>
                $"The new root '{carrier.Into.Value}' is {Said(found.NewRoot)}, and a run does not add to what is "
                + "already there. Empty the ledger, take out of the new root what a run put into it, "
                + "and start over. The new root itself stays: live recordings are written into it.",
            MigrationHindrance.NothingSettlesWhereEncodesGo =>
                "What is carried over is encoded, and that needs exactly one destination that is still "
                + "offered, whose default profile is still offered. Define one and start over.",
            MigrationHindrance.NoLinkCanBeMade =>
                $"A recording is carried by linking it into the new root '{carrier.Into.Value}', and here "
                + $"that link {Said(found.Carrying)}. Nothing was carried. A rehearsal says the same thing "
                + "without stopping.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(hindrance),
                hindrance,
                "What stops a run for real is one of the things a run knows to name."),
        };

    private static string Said(MigrationRootStanding standing)
        => standing is MigrationRootStanding.Missing ? "not there" : "not empty";

    private static string Said(MigrationCarryStanding standing)
        => standing is MigrationCarryStanding.WouldCrossAMount
            ? "would cross a mount, which a hard link cannot do"
            : "cannot be made at all";

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

        Rule made = Rule.CarriedOver(RuleId.New(), conversion.Name, query, clock.GetUtcNow().UtcDateTime);

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

        if (source.Programmed is not { } programme)
        {
            return Instead(verdict, MigrationRefusal.Unidentifiable);
        }

        if (!source.LastsAnyTime)
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
            Recording.CarriedOver(
                id,
                programme,
                source.Name,
                carrier.Into,
                name,
                verdict.Observed ?? 0,
                source.EndAt,
                clock.GetUtcNow().UtcDateTime),
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
}
