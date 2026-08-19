using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class VisitTally
{
    private VisitTally()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public int TableId { get; private set; }

    public int LastTableId { get; private set; }

    public int SegmentsDeclared { get; private set; }

    public int SegmentsHeard { get; private set; }

    public int SectionsDeclared { get; private set; }

    public int SectionsHeard { get; private set; }

    public int VersionChanges { get; private set; }

    public static VisitTally Rehydrate(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        ServiceId serviceId,
        int tableId,
        int lastTableId,
        int segmentsDeclared,
        int segmentsHeard,
        int sectionsDeclared,
        int sectionsHeard,
        int versionChanges)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentsDeclared);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentsHeard);
        ArgumentOutOfRangeException.ThrowIfNegative(sectionsDeclared);
        ArgumentOutOfRangeException.ThrowIfNegative(sectionsHeard);
        ArgumentOutOfRangeException.ThrowIfNegative(versionChanges);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segmentsHeard, segmentsDeclared);

        return new VisitTally
        {
            NetworkId = networkId,
            TransportStreamId = transportStreamId,
            ServiceId = serviceId,
            TableId = tableId,
            LastTableId = lastTableId,
            SegmentsDeclared = segmentsDeclared,
            SegmentsHeard = segmentsHeard,
            SectionsDeclared = sectionsDeclared,
            SectionsHeard = sectionsHeard,
            VersionChanges = versionChanges,
        };
    }

    internal bool Counts(VisitTally other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return ServiceId.Equals(other.ServiceId) && TableId == other.TableId;
    }

    internal void Restate(VisitTally fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);

        LastTableId = fresh.LastTableId;
        SegmentsDeclared = fresh.SegmentsDeclared;
        SegmentsHeard = fresh.SegmentsHeard;
        SectionsDeclared = fresh.SectionsDeclared;
        SectionsHeard = fresh.SectionsHeard;
        VersionChanges = fresh.VersionChanges;
    }
}
