using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;

using Puppis.Contracts;

namespace Carina.Infrastructure.Rules;

public sealed record RuleApplicationRun(
    long Revision,
    int Read,
    IReadOnlyList<Reservation> Made,
    IReadOnlyList<Reservation> Refused,
    IReadOnlyList<Reservation> Revived,
    IReadOnlyList<Reservation> Withdrawn,
    IReadOnlyList<Rule> TurnedOff,
    IReadOnlyList<RuleFault> Faulted,
    int Moved = 0,
    int Rules = 0,
    TimeSpan Took = default);

public sealed record RuleRehearsal(
    IReadOnlyList<RuleMatch> Taking,
    int Shadowed,
    IReadOnlyList<Reservation> Making,
    IReadOnlyList<Reservation> Withdrawing,
    IReadOnlyList<Reservation> Sweeping,
    IReadOnlyList<Reservation> ChangingHands,
    SchedulingRun Settled,
    int Moved = 0);

public sealed record RuleRetirement(
    Rule Rule,
    IReadOnlyList<Reservation> Withdrawn,
    IReadOnlyList<Reservation> Swept);

public sealed class RuleApplicationService(
    IRuleRepository rules,
    IProgrammeRepository programmes,
    IReservationRepository reservations,
    ReservationOutcomeDomainService ledger,
    RuleWithdrawalDomainService withdrawal,
    ReservationSchedulingService scheduling,
    RuleMatcher matcher,
    RuleApplicationSettings settings,
    IAtomicWrite write,
    IAppEventPublisher events,
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
        RuleWithdrawalGuard guard = await withdrawal.GuardAsync(settings.Grace, cancellationToken);

        return await OnceMoreIfMovedAsync(() => DroppedAsync(ruleId, at, guard, cancellationToken));
    }

    public async Task<RuleRetirement?> RetiredAsync(RuleId ruleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        if (await rules.FindAsync(ruleId, cancellationToken) is not { } rule)
        {
            return null;
        }

        DateTime at = Moment();
        RuleWithdrawalGuard guard = await withdrawal.GuardAsync(settings.Grace, cancellationToken);
        IReadOnlyList<Reservation> withdrawn =
            await OnceMoreIfMovedAsync(() => DroppedAsync(ruleId, at, guard, cancellationToken));

        IReadOnlyList<Reservation> swept = await OnceMoreIfMovedAsync(() => write.AllOrNothingAsync(
            async token =>
            {
                RuleRetiring retiring = await withdrawal.RetiringAsync(ruleId, guard, at, token);

                await reservations.WithdrawAsync(retiring.Withdrawn, token);

                foreach (Reservation reservation in retiring.Kept)
                {
                    reservation.LoseRule();
                }

                await reservations.SaveAllAsync(retiring.Kept, token);
                await rules.RemoveAsync(rule, token);

                return retiring.Withdrawn;
            },
            cancellationToken));

        if (swept.Count > 0)
        {
            events.Signal(AppEventName.Reservations);

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
        (IReadOnlyList<Programme> carried, _) = await ReadAsync(0, cancellationToken);
        BroadcastGroupResolver groups = BroadcastGroupResolver.Of(await programmes.ListGroupedAsync(cancellationToken));
        IReadOnlyList<Programme> read = Alongside(carried, groups);
        IReadOnlyList<ProgrammeMatch> guide = ProgrammeSearchMatching.Layered(
            [.. read.Where(programme => programme.IsStillToComeAt(at))],
            []);

        IReadOnlyList<Rule> enabled = await rules.ListEnabledByPrecedenceAsync(cancellationToken);
        Rule[] alongside = [.. enabled.Where(rule => !rule.Id.Equals(draft.Id)), draft];

        RuleMatchRun run = await matcher.AgainstAsync(alongside, guide, cancellationToken);
        IReadOnlyList<Reservation> changed = await ChangedByHandAsync(alongside, at, cancellationToken);
        RuleTaking resolved = RuleMatcher.Resolved(run.Matches, run.Searches, changed, groups, read, at);
        RuleMatch[] taking = [.. resolved.Matches.Where(match => match.Rule.Id.Equals(draft.Id))];
        RuleTaking drafted = RuleMatcher.Resolved(
            [.. run.Matches.Where(match => match.Rule.Id.Equals(draft.Id))],
            run.Searches,
            changed,
            groups,
            read,
            at);

        List<Reservation> making = [];
        List<Reservation> changingHands = [];

        foreach (RuleMatch match in taking)
        {
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

        RuleWithdrawalGuard guard = await withdrawal.GuardAsync(settings.Grace, cancellationToken);
        RuleWithdrawalRehearsal rehearsed = await withdrawal.RehearseAsync(
            draft.Id,
            [.. resolved.Matches.Select(match => ListingKey.Of(match.Programme))],
            guard,
            at,
            cancellationToken);

        return new RuleRehearsal(
            taking,
            await matcher.ShadowedByAsync(draft, guide, cancellationToken),
            making,
            rehearsed.Withdrawing,
            rehearsed.Sweeping,
            changingHands,
            await scheduling.PreviewAsync(making, cancellationToken),
            drafted.Moved);
    }

    private async Task<IReadOnlyList<Reservation>> DroppedAsync(
        RuleId ruleId,
        DateTime at,
        RuleWithdrawalGuard guard,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Reservation> leaving = await withdrawal.SwitchedOffAsync(ruleId, guard, at, cancellationToken);

        await WithdrawAsync(leaving, cancellationToken);

        return leaving;
    }

    /// <summary>
    /// Runs <paramref name="writing"/>, and runs it once more when a reservation it wrote had changed
    /// in the store since it was read.
    /// </summary>
    private static async Task<T> OnceMoreIfMovedAsync<T>(Func<Task<T>> writing)
    {
        try
        {
            return await writing();
        }
        catch (ReservationMovedMeanwhileException)
        {
            return await writing();
        }
    }

    private async Task<RuleApplicationRun> ApplyAsync(
        long from,
        bool sweeping,
        CancellationToken cancellationToken)
    {
        long began = clock.GetTimestamp();
        DateTime at = Moment();
        IReadOnlyList<Rule> enabled = await rules.ListEnabledByPrecedenceAsync(cancellationToken);
        (IReadOnlyList<Programme> carried, long revision) = await ReadAsync(from, cancellationToken);
        BroadcastGroupResolver groups = BroadcastGroupResolver.Of(await programmes.ListGroupedAsync(cancellationToken));
        IReadOnlyList<Programme> read = Alongside(carried, groups);

        RuleMatchRun run = await matcher.AgainstAsync(
            enabled,
            ProgrammeSearchMatching.Layered([.. read.Where(programme => programme.IsStillToComeAt(at))], []),
            cancellationToken);
        RuleTaking resolved = RuleMatcher.Resolved(
            run.Matches,
            run.Searches,
            await ChangedByHandAsync(enabled, at, cancellationToken),
            groups,
            read,
            at);

        foreach (Rule off in run.TurnedOff)
        {
            await rules.SaveAsync(off, cancellationToken);
        }

        Making making = await MakeAsync(resolved.Matches, at, cancellationToken);

        IReadOnlyList<Reservation> withdrawn =
            await LeaveAsync(read, run, resolved, enabled, sweeping, at, cancellationToken);

        return new RuleApplicationRun(
            revision,
            read.Count,
            making.Made,
            making.Refused,
            making.Revived,
            withdrawn,
            run.TurnedOff,
            run.Faulted,
            resolved.Moved,
            enabled.Count,
            clock.GetElapsedTime(began));
    }

    /// <summary>
    /// The reservations still to come that a rule made and a person has since changed: their priority,
    /// a margin or whether they are encoded is no longer what the rule asks. The ledger keeps no mark of
    /// a change, so the values are the only witness, and a rule edited after it made a reservation makes
    /// that reservation look changed too.
    /// </summary>
    private async Task<IReadOnlyList<Reservation>> ChangedByHandAsync(
        IReadOnlyList<Rule> standing,
        DateTime at,
        CancellationToken cancellationToken)
    {
        Dictionary<RuleId, Rule> asked = [];

        foreach (Rule rule in standing)
        {
            asked.TryAdd(rule.Id, rule);
        }

        return
        [
            .. (await reservations.ListPendingAsync(Everything(at), cancellationToken))
                .Where(reservation => reservation.RuleId is { } ruleId
                    && asked.TryGetValue(ruleId, out Rule? rule)
                    && !reservation.IsAsTheRuleAsks(rule)),
        ];
    }

    private async Task<Making> MakeAsync(
        IReadOnlyList<RuleMatch> matches,
        DateTime at,
        CancellationToken cancellationToken)
    {
        var made = new List<Reservation>();
        var refused = new List<Reservation>();
        var revived = new List<Reservation>();

        foreach (RuleMatch match in matches)
        {
            var reference = new ProgrammeRef(
                match.Programme.NetworkId,
                match.Programme.ServiceId,
                match.Programme.EventId,
                match.Programme.StartsAt);

            if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is { } already)
            {
                if (await RevivedAsync(already, at, cancellationToken))
                {
                    revived.Add(already);
                }

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

        return new Making(made, refused, revived);
    }

    /// <summary>
    /// Brings back the cancelled reservation already standing on the key of a broadcast a rule takes
    /// again. Only a reservation a rule made and the guide took out comes back; one a person cancelled
    /// does not.
    /// </summary>
    private async Task<bool> RevivedAsync(
        Reservation already,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (!already.MayBeRevivedByRule(at))
        {
            return false;
        }

        SchedulingRun settled = await scheduling.ReviseAsync(
            already,
            new ReservationRevision { Move = ReservationMove.Restore },
            cancellationToken);

        if (!settled.Settled)
        {
            return false;
        }

        await write.AllOrNothingAsync(
            async token =>
            {
                already.Reappear();

                await reservations.SaveAllAsync([already], token);

                return await ledger.RecordOnceAsync(already, ReservationOutcomeKind.ProgrammeReturned, at, token);
            },
            cancellationToken);

        return true;
    }

    private async Task<IReadOnlyList<Reservation>> LeaveAsync(
        IReadOnlyList<Programme> read,
        RuleMatchRun run,
        RuleTaking resolved,
        IReadOnlyList<Rule> enabled,
        bool sweeping,
        DateTime at,
        CancellationToken cancellationToken)
    {
        RuleWithdrawalGuard guard = await withdrawal.GuardAsync(settings.Grace, cancellationToken);
        var pass = new RulePassReading(
            run.Faulted.Select(fault => fault.Rule.Id).ToHashSet(),
            enabled.Where(rule => rule.Enabled).Select(rule => rule.Id).ToHashSet(),
            [.. resolved.Matches.Select(match => ListingKey.Of(match.Programme))],
            read.Select(ListingKey.Of).ToHashSet(),
            sweeping);
        IReadOnlyList<Reservation> leaving = await withdrawal.LeavingAsync(pass, guard, at, cancellationToken);

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

        events.Signal(AppEventName.Reservations);

        await scheduling.RecalculateAsync(cancellationToken);
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
                at,
                match.Programme.Audio,
                match.Programme.Sounds),
            match.GroupKey,
            match.GroupRole,
            at,
            match.Rule.EncodeWhenRecorded);

    /// <summary>
    /// The programmes read, together with every programme in the guide that shares a relayed or moved
    /// broadcast with one of them, or carries the same broadcast on another service at the same time.
    /// </summary>
    private static IReadOnlyList<Programme> Alongside(IReadOnlyList<Programme> read, BroadcastGroupResolver groups)
    {
        HashSet<ProgrammeId> held = [.. read.Select(programme => programme.Id)];

        return
        [
            .. read,
            .. groups.MembersAlongside(held).Where(member => !held.Contains(member.Id)),
        ];
    }

    private static DateTime Provisionally(DateTime startsAt)
        => startsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced;

    private static ReservationWindow Everything(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private DateTime Moment() => clock.GetUtcNow().UtcDateTime;

    private readonly record struct Making(
        IReadOnlyList<Reservation> Made,
        IReadOnlyList<Reservation> Refused,
        IReadOnlyList<Reservation> Revived);
}
