using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public enum BroadcastExclusion
{
    None = 0,

    Shadow = 1,

    Moved = 2,
}

public sealed record BroadcastTarget(Programme Programme, BroadcastGroupKey? Key, BroadcastGroupRole Role)
{
    public ProgrammeRef Reference
        => new(Programme.NetworkId, Programme.ServiceId, Programme.EventId, Programme.StartsAt);
}

public sealed record BroadcastResolution(IReadOnlyList<BroadcastTarget> Targets, BroadcastExclusion Exclusion);

public sealed record BroadcastPlacement(BroadcastTarget Own, IReadOnlyList<BroadcastTarget> Alongside);

public sealed class BroadcastGroupResolver
{
    private const string RelayPrefix = "relay";

    private const string MovementPrefix = "movement";

    private readonly Dictionary<Node, Programme> present;

    private readonly Grouping relays;

    private readonly Grouping movements;

    private readonly Grouping simulcasts;

    private BroadcastGroupResolver(
        Dictionary<Node, Programme> present,
        Grouping relays,
        Grouping movements,
        Grouping simulcasts)
    {
        this.present = present;
        this.relays = relays;
        this.movements = movements;
        this.simulcasts = simulcasts;
    }

    public static BroadcastGroupResolver Of(IEnumerable<Programme> grouped)
    {
        ArgumentNullException.ThrowIfNull(grouped);

        Dictionary<Node, Programme> present = [];

        foreach (Programme programme in grouped)
        {
            present[Node.Of(programme.Id)] = programme;
        }

        Grouping relays = new();
        Grouping movements = new();
        Grouping simulcasts = new();

        foreach ((Node node, Programme programme) in present)
        {
            foreach (RelatedProgramme related in programme.Related)
            {
                Node other = new(related.NetworkId, related.ServiceId, related.EventId);

                if (related.Kind is RelationKind.Relayed)
                {
                    relays.Join(node, other);
                }
                else if (related.Kind is RelationKind.Moved)
                {
                    movements.Join(node, other);
                }
                else if (related.Kind is RelationKind.Shared && SharedAlike(present, programme, other))
                {
                    simulcasts.Join(node, other);
                }
            }
        }

        foreach (Node[] alike in Alike(present.Values))
        {
            foreach (Node member in alike)
            {
                simulcasts.Join(alike[0], member);
            }
        }

        return new BroadcastGroupResolver(present, relays, movements, simulcasts);
    }

    public IReadOnlyList<Programme> MembersAlongside(IEnumerable<ProgrammeId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        HashSet<Node> found = [];

        foreach (ProgrammeId id in ids)
        {
            Node node = Node.Of(id);

            found.UnionWith(relays.MembersOf(node));
            found.UnionWith(movements.MembersOf(node));
            found.UnionWith(simulcasts.MembersOf(node));
        }

        return
        [
            .. found
                .Where(present.ContainsKey)
                .Order()
                .Select(node => present[node]),
        ];
    }

    public BroadcastResolution Resolve(Programme asked, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(asked);

        Programme held = present.GetValueOrDefault(Node.Of(asked.Id), asked);

        if (held.IsShadow)
        {
            return new BroadcastResolution([], BroadcastExclusion.Shadow);
        }

        Programme primary = PrimaryOf(held);

        return new BroadcastResolution(
            Targets(held, at),
            ReferenceEquals(primary, held) ? BroadcastExclusion.None : BroadcastExclusion.Moved);
    }

    public BroadcastTarget? SimulcastPrimaryOf(BroadcastTarget target, IReadOnlySet<ProgrammeRef>? kept = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        Programme held = present.GetValueOrDefault(Node.Of(target.Programme.Id), target.Programme);
        IReadOnlyList<Programme> cluster = ClustersOf(simulcasts, Node.Of(held.Id))
            .FirstOrDefault(run => run.Contains(held)) ?? [];
        Programme[] chosen = [.. cluster.Where(member => kept?.Contains(Reference(member)) is true)];
        Programme? reserved = chosen.Length > 0
            ? chosen.MinBy(member => Node.Of(member.Id))
            : PrimaryOrNone(cluster);

        return reserved is null || chosen.Contains(held) || ReferenceEquals(reserved, held)
            ? null
            : Target(reserved, reserved);
    }

    public BroadcastPlacement? Place(ProgrammeRef reserved, DateTime endsAt, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reserved);

        Node node = Node.Of(reserved.Id);
        Programme? own;
        Programme reached;

        if (present.TryGetValue(node, out Programme? held))
        {
            if (held.IsShadow)
            {
                return null;
            }

            own = PrimaryOf(held);
            reached = held;
        }
        else
        {
            own = OverlappingPrimary(node, reserved.StartsAt, endsAt);
            reached = own!;
        }

        if (own is null)
        {
            return null;
        }

        IReadOnlyList<BroadcastTarget> targets = Targets(reached, at);
        BroadcastTarget target = targets.FirstOrDefault(candidate => ReferenceEquals(candidate.Programme, own))
            ?? Target(reached, own);

        return new BroadcastPlacement(
            target,
            [.. targets.Where(candidate => !ReferenceEquals(candidate.Programme, own))]);
    }

    private IReadOnlyList<BroadcastTarget> Targets(Programme programme, DateTime at)
    {
        IReadOnlyList<Programme> segments = SegmentsOf(programme, at);
        List<BroadcastTarget> targets = [];
        HashSet<Node> taken = [];

        foreach (Programme segment in segments)
        {
            Programme primary = PrimaryOf(segment);

            if (taken.Add(Node.Of(primary.Id)))
            {
                targets.Add(Target(programme, primary));
            }
        }

        return targets;
    }

    private BroadcastTarget Target(Programme asked, Programme target)
    {
        Node askedNode = Node.Of(asked.Id);

        if (relays.MembersOf(askedNode).Count > 1)
        {
            return new BroadcastTarget(target, relays.KeyOf(askedNode, RelayPrefix), BroadcastGroupRole.RelaySegment);
        }

        Node targetNode = Node.Of(target.Id);

        return movements.MembersOf(targetNode).Count > 1
            ? new BroadcastTarget(
                target,
                movements.KeyOf(targetNode, MovementPrefix),
                BroadcastGroupRole.MovementPrimary)
            : new BroadcastTarget(target, null, BroadcastGroupRole.Standalone);
    }

    private IReadOnlyList<Programme> SegmentsOf(Programme programme, DateTime at)
    {
        IReadOnlySet<Node> members = relays.MembersOf(Node.Of(programme.Id));

        if (members.Count <= 1)
        {
            return EndsOf(programme) > at ? [programme] : [];
        }

        return
        [
            .. members
                .Where(present.ContainsKey)
                .Select(node => present[node])
                .Where(segment => !segment.IsShadow && EndsOf(segment) > at)
                .OrderBy(segment => segment.StartsAt)
                .ThenBy(segment => Node.Of(segment.Id)),
        ];
    }

    private Programme PrimaryOf(Programme programme)
    {
        IReadOnlyList<IReadOnlyList<Programme>> clusters = ClustersOf(movements, Node.Of(programme.Id));

        foreach (IReadOnlyList<Programme> cluster in clusters)
        {
            if (cluster.Any(member => ReferenceEquals(member, programme)))
            {
                return Primary(cluster);
            }
        }

        return programme;
    }

    private Programme? PrimaryOrNone(IReadOnlyList<Programme> cluster)
        => cluster.Count is 0 ? null : PrimaryOf(cluster.MinBy(member => Node.Of(member.Id))!);

    private Programme? OverlappingPrimary(Node node, DateTime startsAt, DateTime endsAt)
    {
        foreach (IReadOnlyList<Programme> cluster in ClustersOf(movements, node))
        {
            if (cluster.Any(member => member.StartsAt < endsAt && EndsOf(member) > startsAt))
            {
                return Primary(cluster);
            }
        }

        return null;
    }

    private IReadOnlyList<IReadOnlyList<Programme>> ClustersOf(Grouping grouping, Node node)
    {
        IReadOnlySet<Node> members = grouping.MembersOf(node);

        if (members.Count <= 1)
        {
            return [];
        }

        Programme[] listed =
        [
            .. members
                .Where(present.ContainsKey)
                .Select(member => present[member])
                .Where(member => !member.IsShadow)
                .OrderBy(member => member.StartsAt)
                .ThenBy(member => Node.Of(member.Id)),
        ];

        List<IReadOnlyList<Programme>> clusters = [];
        List<Programme> running = [];
        DateTime reach = DateTime.MinValue;

        foreach (Programme member in listed)
        {
            if (running.Count > 0 && member.StartsAt >= reach)
            {
                clusters.Add(running);
                running = [];
            }

            running.Add(member);
            reach = running.Count == 1 || EndsOf(member) > reach ? EndsOf(member) : reach;
        }

        if (running.Count > 0)
        {
            clusters.Add(running);
        }

        return clusters;
    }

    private static Programme Primary(IReadOnlyList<Programme> cluster)
        => cluster
            .OrderBy(member => member.Running is ProgrammeRunning.Running ? 0 : 1)
            .ThenBy(member => member.StartsAt)
            .ThenBy(member => Node.Of(member.Id))
            .First();

    private static ProgrammeRef Reference(Programme programme)
        => new(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt);

    private static bool SharedAlike(Dictionary<Node, Programme> present, Programme programme, Node other)
        => !programme.IsShadow
            && other.ServiceId != programme.ServiceId.Value
            && present.TryGetValue(other, out Programme? sharing)
            && !sharing.IsShadow;

    private static IEnumerable<Node[]> Alike(IEnumerable<Programme> programmes)
        => programmes
            .Where(programme => !programme.IsShadow && programme.Name.Length > 0 && programme.EndsAt is not null)
            .GroupBy(programme => (
                programme.NetworkId.Value,
                programme.TransportStreamId.Value,
                programme.StartsAt,
                programme.EndsAt,
                programme.Name))
            .Select(alike => alike.Select(programme => Node.Of(programme.Id)).Order().ToArray())
            .Where(alike => alike.Select(member => member.ServiceId).Distinct().Count() > 1);

    private static DateTime EndsOf(Programme programme)
        => programme.EndsAt ?? programme.StartsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced;

    private readonly record struct Node(int NetworkId, int ServiceId, int EventId) : IComparable<Node>
    {
        public static Node Of(ProgrammeId id) => new(id.NetworkId.Value, id.ServiceId.Value, id.EventId.Value);

        public int CompareTo(Node other)
        {
            int network = NetworkId.CompareTo(other.NetworkId);

            if (network != 0)
            {
                return network;
            }

            int service = ServiceId.CompareTo(other.ServiceId);

            return service != 0 ? service : EventId.CompareTo(other.EventId);
        }

        public override string ToString() => $"{NetworkId}-{ServiceId}-{EventId}";
    }

    private sealed class Grouping
    {
        private static readonly IReadOnlySet<Node> Alone = new HashSet<Node>();

        private readonly Dictionary<Node, Node> parents = [];

        private Dictionary<Node, HashSet<Node>>? sets;

        public void Join(Node one, Node other)
        {
            sets = null;

            Node left = Root(one);
            Node right = Root(other);

            if (left == right)
            {
                return;
            }

            if (right.CompareTo(left) < 0)
            {
                parents[left] = right;
            }
            else
            {
                parents[right] = left;
            }
        }

        public IReadOnlySet<Node> MembersOf(Node node)
            => parents.ContainsKey(node) ? Sets()[Root(node)] : Alone;

        public BroadcastGroupKey KeyOf(Node node, string prefix)
            => new($"{prefix}:{MembersOf(node).Min()}");

        private Dictionary<Node, HashSet<Node>> Sets()
        {
            if (sets is not null)
            {
                return sets;
            }

            sets = [];

            foreach (Node node in parents.Keys.ToList())
            {
                Node root = Root(node);

                if (!sets.TryGetValue(root, out HashSet<Node>? members))
                {
                    members = [];
                    sets[root] = members;
                }

                members.Add(node);
            }

            return sets;
        }

        private Node Root(Node node)
        {
            if (!parents.TryGetValue(node, out Node parent))
            {
                parents[node] = node;

                return node;
            }

            if (parent == node)
            {
                return node;
            }

            Node root = Root(parent);

            parents[node] = root;

            return root;
        }
    }
}
