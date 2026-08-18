using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record HarvestedStream(
    VisitOutcome Outcome,
    ScheduleProgress Progress,
    IReadOnlyList<EventInformationTable> Tables,
    long UnreadablePackets);

public sealed class StreamHarvest
{
    private readonly SectionReader reader = new(EventInformationTable.Pid);

    private readonly List<EventInformationTable> tables = [];

    private readonly ScheduleProgress progress = new();

    public ScheduleProgress Progress => progress;

    public long UnreadablePackets => reader.UnreadablePackets;

    public bool CanLetGo => progress.Completeness is not ScheduleCompleteness.Incomplete;

    public void Push(ReadOnlySpan<byte> packets)
    {
        foreach (var read in reader.Push(packets))
        {
            if (read is not SectionRead.Assembled assembled)
            {
                continue;
            }

            if (EventInformationTable.Read(assembled.Section) is not TableRead<EventInformationTable>.Parsed parsed)
            {
                continue;
            }

            tables.Add(parsed.Table);
            progress.Saw(parsed.Table);
        }
    }

    public HarvestedStream Conclude(bool interrupted, bool anyBytes)
    {
        if (interrupted)
        {
            return Harvested(VisitOutcome.Interrupted);
        }

        if (!anyBytes)
        {
            return Harvested(VisitOutcome.NoBytes);
        }

        return Harvested(progress.Completeness switch
        {
            ScheduleCompleteness.Complete => VisitOutcome.Complete,
            ScheduleCompleteness.BasicOnly => VisitOutcome.BasicOnly,
            _ => VisitOutcome.Incomplete,
        });
    }

    private HarvestedStream Harvested(VisitOutcome outcome)
        => new(outcome, progress, tables, reader.UnreadablePackets);
}
