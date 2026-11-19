using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed class RuleWithdrawalDomainService(
    IReservationRepository reservations,
    IBroadcastStreamDirectory directory,
    IStreamVisitRepository visits,
    ICollectionEpochRepository epochs,
    TimeProvider clock)
{
    public async Task<RuleWithdrawalGuard> GuardAsync(TimeSpan grace, CancellationToken cancellationToken)
    {
        Dictionary<(int NetworkId, int ServiceId), int> carriers = [];

        foreach (BroadcastStream stream in await directory.ListAsync(cancellationToken))
        {
            foreach (ServiceId service in stream.Services)
            {
                carriers[(stream.NetworkId.Value, service.Value)] = stream.TransportStreamId.Value;
            }
        }

        Dictionary<(int NetworkId, int Stream), VisitOutcome> settled = [];
        CollectionEpoch epoch = await epochs.ReadAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

        foreach (StreamVisit visit in await visits.ListAsync(cancellationToken))
        {
            if (epoch.GuideDiscardedAt is { } discarded
                && (visit.LastCompletedAt is not { } completed || completed <= discarded))
            {
                continue;
            }

            settled[(visit.NetworkId.Value, visit.TransportStreamId.Value)] = visit.Outcome;
        }

        return new RuleWithdrawalGuard(carriers, settled, grace);
    }

    public async Task<IReadOnlyList<Reservation>> LeavingAsync(
        RulePassReading pass,
        RuleWithdrawalGuard guard,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(guard);

        IReadOnlyList<Reservation> pending = await reservations.ListPendingAsync(Ahead(at), cancellationToken);
        HashSet<ListingKey> kept = StandingFor(pass.Taken, pending);
        List<Reservation> leaving = [];

        foreach (Reservation reservation in pending)
        {
            if (!await LeavesAsync(reservation, pass, kept, guard, at, cancellationToken))
            {
                continue;
            }

            leaving.Add(reservation);
        }

        return leaving;
    }

    public async Task<IReadOnlyList<Reservation>> SwitchedOffAsync(
        RuleId rule,
        RuleWithdrawalGuard guard,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(guard);

        return
        [
            .. (await reservations.ListForRuleAsync(rule, cancellationToken))
                .Where(reservation => guard.Lets(reservation, RuleWithdrawal.WhenTheRuleIsSwitchedOff, at)),
        ];
    }

    public async Task<RuleRetiring> RetiringAsync(
        RuleId rule,
        RuleWithdrawalGuard guard,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(guard);

        IReadOnlyList<Reservation> standing = await reservations.ListForRuleAsync(rule, cancellationToken);

        return new RuleRetiring(
            [.. standing.Where(reservation => guard.Lets(reservation, RuleWithdrawal.WhenTheRuleIsDeleted, at))],
            [.. standing.Where(reservation => !guard.Lets(reservation, RuleWithdrawal.WhenTheRuleIsDeleted, at))]);
    }

    public async Task<RuleWithdrawalRehearsal> RehearseAsync(
        RuleId draft,
        IReadOnlyList<ListingKey> taken,
        RuleWithdrawalGuard guard,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(guard);

        IReadOnlyList<Reservation> held = await reservations.ListForRuleAsync(draft, cancellationToken);
        HashSet<ListingKey> standing = StandingFor(taken, held);

        return new RuleWithdrawalRehearsal(
            [
                .. held
                    .Where(reservation => !standing.Contains(ListingKey.Of(reservation)))
                    .Where(reservation => guard.Lets(reservation, RuleWithdrawal.WhileTheRuleStands, at)),
            ],
            [.. held.Where(reservation => guard.Lets(reservation, RuleWithdrawal.WhenTheRuleIsDeleted, at))]);
    }

    public static HashSet<ListingKey> StandingFor(IEnumerable<ListingKey> taken, IEnumerable<Reservation> held)
    {
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(held);

        ILookup<(int NetworkId, int ServiceId, int EventId), ListingKey> holding =
            held.Select(ListingKey.Of).Distinct().ToLookup(key => key.Broadcast);
        HashSet<ListingKey> standing = [];

        foreach (ListingKey listing in taken)
        {
            IEnumerable<ListingKey> ofTheBroadcast = holding[listing.Broadcast];

            if (ofTheBroadcast.Contains(listing))
            {
                standing.Add(listing);

                continue;
            }

            standing.UnionWith(ofTheBroadcast);
        }

        return standing;
    }

    private static ReservationWindow Ahead(DateTime at)
        => new(at - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private async Task<bool> LeavesAsync(
        Reservation reservation,
        RulePassReading pass,
        HashSet<ListingKey> kept,
        RuleWithdrawalGuard guard,
        DateTime at,
        CancellationToken cancellationToken)
    {
        RuleId? rule = reservation.RuleId;

        if (rule is { } faltered && pass.Faulted.Contains(faltered))
        {
            return false;
        }

        bool holds = rule is { } held && pass.Standing.Contains(held);
        ListingKey listing = ListingKey.Of(reservation);

        if (holds && (kept.Contains(listing) || (!pass.Sweeping && !pass.Seen.Contains(listing))))
        {
            return false;
        }

        RuleWithdrawal occasion = holds ? RuleWithdrawal.WhileTheRuleStands : RuleWithdrawal.WhenTheRuleIsSwitchedOff;

        if (!guard.Lets(reservation, occasion, at))
        {
            return false;
        }

        return !holds || !await ContinuesARelayAsync(reservation, kept, cancellationToken);
    }

    private async Task<bool> ContinuesARelayAsync(
        Reservation reservation,
        HashSet<ListingKey> kept,
        CancellationToken cancellationToken)
    {
        if (reservation.BroadcastGroupRole is not BroadcastGroupRole.RelaySegment
            || reservation.BroadcastGroupKey is not { } key)
        {
            return false;
        }

        IReadOnlyList<Reservation> segments = await reservations.ListForBroadcastGroupAsync(key, cancellationToken);

        return segments.Any(segment => !segment.Id.Equals(reservation.Id)
                                       && (segment.StartedAt is not null
                                           || segment.RecordingOutcome is not null
                                           || kept.Contains(ListingKey.Of(segment))));
    }
}
