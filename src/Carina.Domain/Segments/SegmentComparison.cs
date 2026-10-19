using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

/// <summary>
/// One recording judged: its station, what the catalog is sure of in it, and the detection made of it without the
/// catalog.
/// </summary>
public sealed record SegmentComparison
{
    public SegmentComparison(NetworkId networkId, ServiceId serviceId, CatalogTruth truth, SegmentDetection detection)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(truth);
        ArgumentNullException.ThrowIfNull(detection);

        if (!truth.Judges)
        {
            throw new ArgumentException("A recording is judged against something the catalog is sure of in it.", nameof(truth));
        }

        NetworkId = networkId;
        ServiceId = serviceId;
        Truth = truth;
        Detection = detection;
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public CatalogTruth Truth { get; }

    public SegmentDetection Detection { get; }

    /// <summary>
    /// The stretches a viewer is shown: none of a detection thrown away.
    /// </summary>
    public IReadOnlyList<SegmentSpan> Shown => Detection.State is SegmentDetectionState.Discarded ? [] : Detection.Segments;
}
