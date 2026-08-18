namespace Carina.Broadcast.Tables;

public enum ScheduleCompleteness
{
    Incomplete = 0,

    BasicOnly = 1,

    Complete = 2,
}

public sealed record ScheduledService(int NetworkId, int TransportStreamId, int ServiceId);

public sealed class ScheduleProgress
{
    public const int SectionsPerSegment = 8;

    private const int ExtendedOffset = 8;

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

        if (!tables.TryGetValue(key, out TableProgress? progress) || progress.Version != table.VersionNumber)
        {
            progress = new TableProgress(table.VersionNumber, table.LastTableId, table.LastSectionNumber);
            tables[key] = progress;
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

        return IsWhole(service, EventInformationTable.FirstScheduleActualTableId + ExtendedOffset)
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
            if (!tables.TryGetValue((service, tableId), out TableProgress? progress) || progress.Awaited().Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<int> SegmentsAwaited(ScheduledService service, int tableId)
    {
        ArgumentNullException.ThrowIfNull(service);

        return tables.TryGetValue((service, tableId), out TableProgress? progress) ? progress.Awaited() : [];
    }

    private sealed class TableProgress(int version, int lastTableId, int lastSectionNumber)
    {
        private readonly Dictionary<int, int> lastOfSegment = [];

        private readonly HashSet<int> sections = [];

        public int Version { get; } = version;

        public int LastTableId { get; } = lastTableId;

        public void Saw(int sectionNumber, int segmentLastSectionNumber)
        {
            sections.Add(sectionNumber);
            lastOfSegment[sectionNumber / SectionsPerSegment] = segmentLastSectionNumber;
        }

        public IReadOnlyList<int> Awaited()
        {
            var awaited = new List<int>();

            for (int segment = 0; segment <= lastSectionNumber / SectionsPerSegment; segment++)
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
