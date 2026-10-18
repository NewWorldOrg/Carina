namespace Carina.Domain.Segments;

/// <summary>
/// Detects the breaks in one recording with one version of the model: finds the boundary candidates,
/// computes the features of the segments between them, scores each segment as a break by logistic
/// regression, decides the sequence by dynamic programming and judges it. The logo is read only where it
/// tells programme from break.
/// </summary>
public static class SegmentDetector
{
    public static SegmentDetection Detect(SegmentEvidence evidence, SegmentContext context, SegmentModel model)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);

        bool[]? sightings = context.Logo.Telling(evidence.Outlines);
        IReadOnlyList<BoundaryCandidate> candidates = BoundaryCandidates.Find(evidence, context, sightings, model.Thresholds);
        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(candidates, evidence, context, sightings, model.Thresholds);
        double[] scores = [.. pieces.Select(piece => model.BreakScore(piece.Features, context.Station, context.Series))];
        SegmentKind[] kinds = SegmentSequence.Decide(pieces, scores, model.Thresholds);

        return SegmentDetection.Judged(
            SegmentSequence.Spans(pieces, kinds, scores, evidence.Length, model.Thresholds),
            evidence.Length,
            BoundaryCandidates.Snaps(candidates));
    }
}
