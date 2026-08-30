using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;

namespace Carina.Infrastructure.Rules;

public sealed record RuleApplicationRun(
    long Revision,
    int Read,
    IReadOnlyList<Reservation> Made,
    IReadOnlyList<Reservation> Refused,
    IReadOnlyList<Reservation> Withdrawn,
    IReadOnlyList<Rule> TurnedOff,
    IReadOnlyList<RuleFault> Faulted);

public sealed record RuleRehearsal(
    IReadOnlyList<RuleMatch> Taking,
    int Shadowed,
    IReadOnlyList<Reservation> Making,
    IReadOnlyList<Reservation> Withdrawing,
    IReadOnlyList<Reservation> ChangingHands,
    SchedulingRun Settled);

public sealed record RuleRetirement(
    Rule Rule,
    IReadOnlyList<Reservation> Withdrawn,
    IReadOnlyList<Reservation> Swept);

public sealed class RuleApplicationService(
    IRuleRepository rules,
    IProgrammeRepository programmes,
    IReservationRepository reservations,
    IStreamVisitRepository visits,
    IBroadcastStreamDirectory directory,
    ReservationSchedulingService scheduling,
    RuleMatcher matcher,
    RuleApplicationSettings settings,
    IAtomicWrite write,
    TimeProvider clock)
{
    public Task<RuleApplicationRun> SinceAsync(long revision, CancellationToken cancellationToken)
        => ApplyAsync(revision, sweeping: false, cancellationToken);

    public Task<RuleApplicationRun> EverythingAsync(CancellationToken cancellationToken)
        => ApplyAsync(0, sweeping: true, cancellationToken);

    public async Task<IReadOnlyList<Reservation>> DroppedAsync(
        RuleId ruleId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        DateTime at = Moment();
        WithdrawalGuard guard = await GuardAsync(cancellationToken);

        Reservation[] leaving =
        [
            .. (await reservations.ListForRuleAsync(ruleId, cancellationToken))
                .Where(reservation => guard.Lets(reservation, standing: false, at)),
        ];

        await WithdrawAsync(leaving, cancellationToken);

        return leaving;
    }

    public async Task<RuleRetirement?> RetiredAsync(RuleId ruleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        if (await rules.FindAsync(ruleId, cancellationToken) is not { } rule)
        {
            return null;
        }

        IReadOnlyList<Reservation> withdrawn = await DroppedAsync(ruleId, cancellationToken);

        IReadOnlyList<Reservation> swept = await write.AllOrNothingAsync(
            async token =>
            {
                IReadOnlyList<Reservation> standing = await reservations.ListForRuleAsync(ruleId, token);
                Reservation[] left = [.. standing.Where(Orphaning)];
                Reservation[] kept = [.. standing.Where(reservation => !Orphaning(reservation))];

                await reservations.WithdrawAsync(left, token);

                foreach (Reservation reservation in kept)
                {
                    reservation.LoseRule();
                }

                await reservations.SaveAllAsync(kept, token);
                await rules.RemoveAsync(rule, token);

                return left;
            },
            cancellationToken);

        if (swept.Count > 0)
        {
            await scheduling.RecalculateAsync(cancellationToken);
        }

        return new RuleRetirement(rule, withdrawn, swept);
    }

    public async Task<RuleRehearsal?> RehearsedAsync(Rule draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (ProgrammeSearchQuery.Read(draft.Query.Value) is null)
        {
            return null;
        }

        DateTime at = Moment();
        (IReadOnlyList<Programme> read, _) = await ReadAsync(0, cancellationToken);
        IReadOnlyList<ProgrammeMatch> guide = ProgrammeSearchMatching.Layered(
            [.. read.Where(programme => StillToCome(programme, at))],
            []);

        IReadOnlyList<Rule> enabled = await rules.ListEnabledByPrecedenceAsync(cancellationToken);
        Rule[] alongside = [.. enabled.Where(rule => !rule.Id.Equals(draft.Id)), draft];

        RuleMatchRun run = await matcher.AgainstAsync(alongside, guide, cancellationToken);
        RuleMatch[] taking = [.. run.Matches.Where(match => match.Rule.Id.Equals(draft.Id))];

        var making = new List<Reservation>();
        var changingHands = new List<Reservation>();
        var kept = new HashSet<ProgrammeKey>();

        foreach (RuleMatch match in taking)
        {
            kept.Add(Naming(match.Programme));

            var reference = new ProgrammeRef(
                match.Programme.NetworkId,
                match.Programme.ServiceId,
                match.Programme.EventId,
                match.Programme.StartsAt);

            if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is not { } already)
            {
                making.Add(Planned(match, reference, at));

                continue;
            }

            if (already.IsRuleBorn && !draft.Id.Equals(already.RuleId))
            {
                changingHands.Add(already);
            }
        }

        WithdrawalGuard guard = await GuardAsync(cancellationToken);
        Reservation[] withdrawing =
        [
            .. (await reservations.ListForRuleAsync(draft.Id, cancellationToken))
                .Where(reservation => !kept.Contains(Naming(reservation)))
                .Where(reservation => guard.Lets(reservation, standing: true, at)),
        ];

        return new RuleRehearsal(
            taking,
            await matcher.ShadowedByAsync(draft, guide, cancellationToken),
            making,
            withdrawing,
            changingHands,
            await scheduling.PreviewAsync(making, cancellationToken));
    }

    private static bool Orphaning(Reservation reservation)
        => reservation.IsRuleBorn
            && reservation.State is ReservationState.Scheduled or ReservationState.Conflict
            && !reservation.IsPinned;

    private async Task<RuleApplicationRun> ApplyAsync(
        long from,
        bool sweeping,
        CancellationToken cancellationToken)
    {
        DateTime at = Moment();
        IReadOnlyList<Rule> enabled = await rules.ListEnabledByPrecedenceAsync(cancellationToken);
        (IReadOnlyList<Programme> read, long revision) = await ReadAsync(from, cancellationToken);

        RuleMatchRun run = await matcher.AgainstAsync(
            enabled,
            ProgrammeSearchMatching.Layered([.. read.Where(programme => StillToCome(programme, at))], []),
            cancellationToken);

        foreach (Rule off in run.TurnedOff)
        {
            await rules.SaveAsync(off, cancellationToken);
        }

        (IReadOnlyList<Reservation> made, IReadOnlyList<Reservation> refused) =
            await MakeAsync(run.Matches, at, cancellationToken);

        IReadOnlyList<Reservation> withdrawn = await LeaveAsync(read, run, enabled, sweeping, at, cancellationToken);

        return new RuleApplicationRun(
            revision,
            read.Count,
            made,
            refused,
            withdrawn,
            run.TurnedOff,
            run.Faulted);
    }

    private async Task<(IReadOnlyList<Reservation> Made, IReadOnlyList<Reservation> Refused)> MakeAsync(
        IReadOnlyList<RuleMatch> matches,
        DateTime at,
        CancellationToken cancellationToken)
    {
        var made = new List<Reservation>();
        var refused = new List<Reservation>();

        foreach (RuleMatch match in matches)
        {
            var reference = new ProgrammeRef(
                match.Programme.NetworkId,
                match.Programme.ServiceId,
                match.Programme.EventId,
                match.Programme.StartsAt);

            if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is not null)
            {
                continue;
            }

            Reservation planned = Planned(match, reference, at);
            SchedulingRun settled = await scheduling.CreateAsync(planned, cancellationToken);

            if (settled.Settled)
            {
                made.Add(planned);

                continue;
            }

            refused.Add(planned);
        }

        return (made, refused);
    }

    private async Task<IReadOnlyList<Reservation>> LeaveAsync(
        IReadOnlyList<Programme> read,
        RuleMatchRun run,
        IReadOnlyList<Rule> enabled,
        bool sweeping,
        DateTime at,
        CancellationToken cancellationToken)
    {
        WithdrawalGuard guard = await GuardAsync(cancellationToken);
        var faulted = run.Faulted.Select(fault => fault.Rule.Id).ToHashSet();
        var standing = enabled.Where(rule => rule.Enabled).Select(rule => rule.Id).ToHashSet();
        var kept = run.Matches.Select(match => Naming(match.Programme)).ToHashSet();
        var seen = read.Select(Naming).ToHashSet();
        var leaving = new List<Reservation>();

        foreach (Reservation reservation in await reservations.ListPendingAsync(Everything(at), cancellationToken))
        {
            RuleId? ruleId = reservation.RuleId;

            if (ruleId is { } faltered && faulted.Contains(faltered))
            {
                continue;
            }

            bool holds = ruleId is { } held && standing.Contains(held);
            ProgrammeKey naming = Naming(reservation);

            if (holds && (kept.Contains(naming) || (!sweeping && !seen.Contains(naming))))
            {
                continue;
            }

            if (!guard.Lets(reservation, holds, at))
            {
                continue;
            }

            leaving.Add(reservation);
        }

        await WithdrawAsync(leaving, cancellationToken);

        return leaving;
    }

    private async Task WithdrawAsync(IReadOnlyList<Reservation> leaving, CancellationToken cancellationToken)
    {
        if (leaving.Count is 0)
        {
            return;
        }

        await reservations.WithdrawAsync(leaving, cancellationToken);
        await scheduling.RecalculateAsync(cancellationToken);
    }

    private async Task<WithdrawalGuard> GuardAsync(CancellationToken cancellationToken)
    {
        Dictionary<ServiceKey, int> carriers = [];

        foreach (BroadcastStream stream in await directory.ListAsync(cancellationToken))
        {
            foreach (ServiceId service in stream.Services)
            {
                carriers[new ServiceKey(stream.NetworkId.Value, service.Value)] = stream.TransportStreamId.Value;
            }
        }

        Dictionary<ServiceKey, VisitOutcome> settled = [];

        foreach (StreamVisit visit in await visits.ListAsync(cancellationToken))
        {
            settled[new ServiceKey(visit.NetworkId.Value, visit.TransportStreamId.Value)] = visit.Outcome;
        }

        return new WithdrawalGuard(carriers, settled, settings.Grace);
    }

    private async Task<(IReadOnlyList<Programme> Read, long Revision)> ReadAsync(
        long from,
        CancellationToken cancellationToken)
    {
        var carried = new List<Programme>();
        long cursor = from;

        while (true)
        {
            IReadOnlyList<Programme> page = await programmes.ListAfterAsync(cursor, settings.Rows, cancellationToken);

            if (page.Count is 0)
            {
                break;
            }

            carried.AddRange(page);
            cursor = page[^1].Revision;

            if (page.Count < settings.Rows)
            {
                break;
            }
        }

        return (carried, cursor);
    }

    private static Reservation Planned(RuleMatch match, ProgrammeRef reference, DateTime at)
        => Reservation.Plan(
            ReservationId.New(),
            reference,
            match.Rule.Id,
            match.Rule.Priority,
            match.Programme.StartsAt,
            match.Programme.EndsAt ?? Provisionally(match.Programme.StartsAt),
            match.Programme.EndsAt is not null,
            match.Rule.MarginBefore,
            match.Rule.MarginAfter,
            ProgrammeSnapshot.Of(
                match.Programme.Name,
                match.Programme.Summary,
                match.Programme.Items,
                match.Programme.Genres,
                at),
            null,
            BroadcastGroupRole.Standalone,
            at);

    private static bool StillToCome(Programme programme, DateTime at)
        => (programme.EndsAt ?? Provisionally(programme.StartsAt)) > at;

    private static DateTime Provisionally(DateTime startsAt)
        => startsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced;

    private static ReservationWindow Everything(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private static ProgrammeKey Naming(Programme programme)
        => new(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);

    private static ProgrammeKey Naming(ProgrammeMatch programme)
        => new(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);

    private static ProgrammeKey Naming(Reservation reservation)
        => new(
            reservation.NetworkId.Value,
            reservation.ServiceId.Value,
            reservation.EventId.Value,
            reservation.ProgrammeStartsAt);

    private DateTime Moment() => clock.GetUtcNow().UtcDateTime;

    private readonly record struct ProgrammeKey(int NetworkId, int ServiceId, int EventId, DateTime StartsAt);

    private readonly record struct ServiceKey(int NetworkId, int Carried);

    private sealed record WithdrawalGuard(
        IReadOnlyDictionary<ServiceKey, int> Carriers,
        IReadOnlyDictionary<ServiceKey, VisitOutcome> Settled,
        TimeSpan Grace)
    {
        public bool Lets(Reservation reservation, bool standing, DateTime at)
            => reservation.IsRuleBorn
                && reservation.State is ReservationState.Scheduled or ReservationState.Conflict
                && !reservation.IsPinned
                && Collected(reservation)
                && (!standing || reservation.EffectiveStartAt - at > Grace);

        private bool Collected(Reservation reservation)
            => Carriers.TryGetValue(
                    new ServiceKey(reservation.NetworkId.Value, reservation.ServiceId.Value),
                    out int carrier)
                && Settled.TryGetValue(
                    new ServiceKey(reservation.NetworkId.Value, carrier),
                    out VisitOutcome outcome)
                && outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly;
    }
}
