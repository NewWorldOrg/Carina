using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public sealed class StreamDescriptorSection
{
    public const int TableId = 0x3D;

    private StreamDescriptorSection(
        Section section,
        IReadOnlyList<Descriptor> descriptors,
        IReadOnlyList<GeneralEvent> events,
        IReadOnlyList<NptReference> nptReferences)
    {
        TableIdExtension = section.TableIdExtension;
        VersionNumber = section.VersionNumber;
        IsCurrent = section.IsCurrent;
        Descriptors = descriptors;
        Events = events;
        NptReferences = nptReferences;
    }

    public int TableIdExtension { get; }

    public int DataEventId => TableIdExtension >> 12;

    public int EventMessageGroupId => TableIdExtension & 0x0FFF;

    public int VersionNumber { get; }

    public bool IsCurrent { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }

    public IReadOnlyList<GeneralEvent> Events { get; }

    public IReadOnlyList<NptReference> NptReferences { get; }

    public static TableRead<StreamDescriptorSection> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId)
        {
            return Rejected(TableDefect.WrongTableId);
        }

        if (!DescriptorLoop.TryRead(section.Body, out IReadOnlyList<Descriptor>? descriptors))
        {
            return Rejected(TableDefect.MalformedDescriptor);
        }

        var events = new List<GeneralEvent>();
        var references = new List<NptReference>();

        foreach (Descriptor descriptor in descriptors)
        {
            if (!TryTake(descriptor, events, references))
            {
                return Rejected(TableDefect.MalformedDescriptor);
            }
        }

        return new TableRead<StreamDescriptorSection>.Parsed(new StreamDescriptorSection(section, descriptors, events, references));
    }

    private static bool TryTake(Descriptor descriptor, List<GeneralEvent> events, List<NptReference> references)
    {
        switch (descriptor.Tag)
        {
            case StreamDescriptorTags.GeneralEvent when GeneralEvent.TryRead(descriptor, out GeneralEvent? read):
                events.Add(read);

                return true;
            case StreamDescriptorTags.NptReference when NptReference.TryRead(descriptor, out NptReference? reference):
                references.Add(reference);

                return true;
            case StreamDescriptorTags.GeneralEvent or StreamDescriptorTags.NptReference:
                return false;
            default:
                return true;
        }
    }

    private static TableRead<StreamDescriptorSection> Rejected(TableDefect defect)
        => new TableRead<StreamDescriptorSection>.Rejected(defect);
}
