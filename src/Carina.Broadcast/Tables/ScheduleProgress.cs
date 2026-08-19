namespace Carina.Broadcast.Tables;

public enum ScheduleCompleteness
{
    Incomplete = 0,

    BasicOnly = 1,

    Complete = 2,
}

public sealed record ScheduledService(int NetworkId, int TransportStreamId, int ServiceId);

public sealed record ScheduleTally(
    ScheduledService Service,
    int TableId,
    int LastTableId,
    int SegmentsDeclared,
    int SegmentsHeard,
    int SectionsDeclared,
    int SectionsHeard,
    int VersionChanges);

public sealed class ScheduleProgress(TimeProvider clock)
{
    public const int SectionsPerSegment = 8;

    public const int SegmentsPerDay = 8;

    private const int TablesPerSchedule = 8;

    private const int SegmentsPerTable = 32;

    private static readonly TimeSpan SegmentSpan = TimeSpan.FromHours(3);

    private readonly Dictionary<(ScheduledService Service, int TableId), TableProgress> tables = [];

    private readonly List<ScheduledService> services = [];

    public IReadOnlyList<ScheduledService> Services => services;

    public ScheduleCompleteness Completeness
    {
        get
        {
            if (services.Count == 0)
            {
                return ScheduleCompleteness.Incomplete;
            }

            ScheduleCompleteness least = ScheduleCompleteness.Complete;

            foreach (ScheduledService service in services)
            {
                ScheduleCompleteness reached = CompletenessOf(service);

                if (reached < least)
                {
                    least = reached;
                }
            }

            return least;
        }
    }

    public void Saw(EventInformationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.IsPresentFollowing)
        {
            return;
        }

        var service = new ScheduledService(table.OriginalNetworkId, table.TransportStreamId, table.ServiceId);
        (ScheduledService service, int TableId) key = (service, table.TableId);

        if (!tables.TryGetValue(key, out TableProgress? progress))
        {
            progress = new TableProgress(table.VersionNumber, table.LastTableId, table.LastSectionNumber);
            tables[key] = progress;
        }
        else if (progress.Version != table.VersionNumber)
        {
            progress.Renew(table.VersionNumber, table.LastTableId, table.LastSectionNumber);
        }

        if (!services.Contains(service))
        {
            services.Add(service);
        }

        progress.Saw(table.SectionNumber, table.SegmentLastSectionNumber);
    }

    public ScheduleCompleteness CompletenessOf(ScheduledService service)
    {
        if (!IsWhole(service, EventInformationTable.FirstScheduleActualTableId))
        {
            return ScheduleCompleteness.Incomplete;
        }

        return IsWhole(service, EventInformationTable.FirstScheduleActualTableId + TablesPerSchedule)
            ? ScheduleCompleteness.Complete
            : ScheduleCompleteness.BasicOnly;
    }

    public bool IsWhole(ScheduledService service, int firstTableId)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!tables.TryGetValue((service, firstTableId), out TableProgress? first))
        {
            return false;
        }

        if (first.LastTableId < firstTableId || first.LastTableId > EventInformationTable.LastScheduleActualTableId)
        {
            return false;
        }

        for (int tableId = firstTableId; tableId <= first.LastTableId; tableId++)
        {
            if (!tables.TryGetValue((service, tableId), out TableProgress? progress)
                || progress.Awaited(FirstSegmentStillToCome(tableId)).Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<ScheduleTally> Tally()
        =>
        [
            .. tables
                .Select(entry => new ScheduleTally(
                    entry.Key.Service,
                    entry.Key.TableId,
                    entry.Value.LastTableId,
                    entry.Value.SegmentsDeclared,
                    entry.Value.SegmentsHeard,
                    entry.Value.SectionsDeclared,
                    entry.Value.SectionsHeard,
                    entry.Value.VersionChanges))
                .OrderBy(counted => counted.Service.ServiceId)
                .ThenBy(counted => counted.TableId),
        ];

    public IReadOnlyList<int> SegmentsAwaited(ScheduledService service, int tableId)
    {
        ArgumentNullException.ThrowIfNull(service);

        return tables.TryGetValue((service, tableId), out TableProgress? progress)
            ? progress.Awaited(FirstSegmentStillToCome(tableId))
            : [];
    }

    private int FirstSegmentStillToCome(int tableId)
    {
        TimeSpan sinceMidnight = clock.GetUtcNow().ToOffset(BroadcastTime.Offset).TimeOfDay;
        int gone = (int)(sinceMidnight.Ticks / SegmentSpan.Ticks);
        int firstOfTable = (tableId % TablesPerSchedule) * SegmentsPerTable;

        return Math.Max(0, gone - firstOfTable);
    }

    private sealed class TableProgress(int version, int lastTableId, int lastSectionNumber)
    {
        private readonly Dictionary<int, int> lastOfSegment = [];

        private readonly HashSet<int> sections = [];

        private int lastSectionNumber = lastSectionNumber;

        public int Version { get; private set; } = version;

        public int LastTableId { get; private set; } = lastTableId;

        public int VersionChanges { get; private set; }

        public int SegmentsDeclared => (lastSectionNumber / SectionsPerSegment) + 1;

        public int SegmentsHeard => lastOfSegment.Count;

        public int SectionsDeclared
            => lastOfSegment.Sum(segment => segment.Value - (segment.Key * SectionsPerSegment) + 1);

        public int SectionsHeard => sections.Count;

        public void Renew(int renewedVersion, int renewedLastTableId, int renewedLastSectionNumber)
        {
            Version = renewedVersion;
            LastTableId = renewedLastTableId;
            VersionChanges++;
            lastSectionNumber = renewedLastSectionNumber;
            lastOfSegment.Clear();
            sections.Clear();
        }

        public void Saw(int sectionNumber, int segmentLastSectionNumber)
        {
            sections.Add(sectionNumber);
            lastOfSegment[sectionNumber / SectionsPerSegment] = segmentLastSectionNumber;
        }

        public IReadOnlyList<int> Awaited(int from)
        {
            var awaited = new List<int>();

            for (int segment = from; segment <= lastSectionNumber / SectionsPerSegment; segment++)
            {
                if (!lastOfSegment.TryGetValue(segment, out int last))
                {
                    awaited.Add(segment);

                    continue;
                }

                for (int section = segment * SectionsPerSegment; section <= last; section++)
                {
                    if (!sections.Contains(section))
                    {
                        awaited.Add(segment);

                        break;
                    }
                }
            }

            return awaited;
        }
    }
}
