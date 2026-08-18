namespace Carina.Broadcast.Tables;

public enum ScheduleCompleteness
{
    Incomplete = 0,

    BasicOnly = 1,

    Complete = 2,
}

public sealed class ScheduleProgress
{
    public const int SectionsPerSegment = 8;

    private const int ExtendedOffset = 8;

    private readonly Dictionary<int, TableProgress> tables = [];

    public ScheduleCompleteness Completeness
    {
        get
        {
            if (!IsWhole(EventInformationTable.FirstScheduleActualTableId))
            {
                return ScheduleCompleteness.Incomplete;
            }

            return IsWhole(EventInformationTable.FirstScheduleActualTableId + ExtendedOffset)
                ? ScheduleCompleteness.Complete
                : ScheduleCompleteness.BasicOnly;
        }
    }

    public void Saw(EventInformationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.IsPresentFollowing)
        {
            return;
        }

        if (!tables.TryGetValue(table.TableId, out var progress) || progress.Version != table.VersionNumber)
        {
            progress = new TableProgress(table.VersionNumber, table.LastTableId, table.LastSectionNumber);
            tables[table.TableId] = progress;
        }

        progress.Saw(table.SectionNumber, table.SegmentLastSectionNumber);
    }

    public bool IsWhole(int firstTableId)
    {
        if (!tables.TryGetValue(firstTableId, out var first))
        {
            return false;
        }

        for (var tableId = firstTableId; tableId <= first.LastTableId; tableId++)
        {
            if (!tables.TryGetValue(tableId, out var progress) || progress.Awaited().Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<int> SegmentsAwaited(int tableId)
        => tables.TryGetValue(tableId, out var progress) ? progress.Awaited() : [];

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

            for (var segment = 0; segment <= lastSectionNumber / SectionsPerSegment; segment++)
            {
                if (!lastOfSegment.TryGetValue(segment, out var last))
                {
                    awaited.Add(segment);

                    continue;
                }

                for (var section = segment * SectionsPerSegment; section <= last; section++)
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
