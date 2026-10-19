using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

/// <summary>
/// One confirmed recording judged: its station, the stretches its answer lays end to end, the detection made of
/// it without what it is judged by, and whether the answer could name an opening and an ending.
/// </summary>
public sealed record SegmentComparison
{
    public SegmentComparison(
        NetworkId networkId,
        ServiceId serviceId,
        IReadOnlyList<SegmentSpan> answer,
        SegmentDetection detection,
        bool answerNamesOpeningsAndEndings = false)
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
        AnswerNamesOpeningsAndEndings = answerNamesOpeningsAndEndings;
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public IReadOnlyList<SegmentSpan> Answer { get; }

    public SegmentDetection Detection { get; }

    public bool AnswerNamesOpeningsAndEndings { get; }

    public TimeSpan Length => Answer[^1].Ends;

    /// <summary>
    /// The stretches a viewer is shown: none of a detection thrown away.
    /// </summary>
    public IReadOnlyList<SegmentSpan> Shown => Detection.State is SegmentDetectionState.Discarded ? [] : Detection.Segments;

    /// <summary>
    /// The stretches shown as the answer can judge them: an opening or an ending as the programme, where the answer
    /// could not name either, so that one found where the answer says programme is neither an extra boundary nor
    /// programme skipped, and a boundary from one into a break is the boundary from the programme the answer gives.
    /// </summary>
    public IReadOnlyList<SegmentSpan> Judged
        => AnswerNamesOpeningsAndEndings
            ? Shown
            : [.. Shown.Select(stretch => stretch.Kind is SegmentKind.Opening or SegmentKind.Ending ? new SegmentSpan(stretch.Starts, stretch.Ends, SegmentKind.Programme, stretch.Probability) : stretch)];
}
