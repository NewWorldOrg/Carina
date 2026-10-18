using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

/// <summary>
/// One confirmed recording judged: its station, the stretches its answer lays end to end, and the
/// detection made of it without what it is judged by.
/// </summary>
public sealed record SegmentComparison
{
    public SegmentComparison(NetworkId networkId, ServiceId serviceId, IReadOnlyList<SegmentSpan> answer, SegmentDetection detection)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(detection);

        if (answer.Count is 0 || answer.Any(segment => segment is null))
        {
            throw new ArgumentException("A recording is judged against an answer of some stretches.", nameof(answer));
        }

        NetworkId = networkId;
        ServiceId = serviceId;
        Answer = [.. answer];
        Detection = detection;
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public IReadOnlyList<SegmentSpan> Answer { get; }

    public SegmentDetection Detection { get; }

    public TimeSpan Length => Answer[^1].Ends;

    /// <summary>
    /// The stretches a viewer is shown: none of a detection thrown away.
    /// </summary>
    public IReadOnlyList<SegmentSpan> Shown => Detection.State is SegmentDetectionState.Discarded ? [] : Detection.Segments;
}
