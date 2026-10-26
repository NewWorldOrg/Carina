namespace Carina.Domain.Segments;

/// <summary>
/// The segments of one recording read with some thresholds, ready to be scored: the pieces between its
/// boundary candidates with their features, the candidates' seconds, its length, the station and
/// series its offsets are kept by, the neighbouring pieces read as one (<see cref="JoinedPiece"/>), and the
/// candidates themselves.
/// </summary>
public sealed record RecordingPieces(IReadOnlyList<SegmentPiece> Pieces, IReadOnlyList<TimeSpan> Snaps, TimeSpan Length, string Station, string? Series)
{
    public IReadOnlyList<JoinedPiece> Joined { get; init; } = [];

    public IReadOnlyList<BoundaryCandidate> Candidates { get; init; } = [];
}

/// <summary>
/// Detects the breaks, the opening and the ending in one recording with one version of the model: finds the
/// boundary candidates, computes the features of the segments between them, scores how likely each segment is
/// to be each kind the version tells apart by logistic regression, and so of the neighbouring segments read as one
/// (<see cref="JoinedPiece"/>), decides the sequence by dynamic programming with the segments the catalog hears fixed to
/// their kind, and judges it. The logo is read only where it
/// tells programme from break. Reading the segments and scoring them are apart, so that one reading
/// serves every model learned with the same thresholds.
/// </summary>
public static class SegmentDetector
{
    public static SegmentDetection Detect(SegmentEvidence evidence, SegmentContext context, SegmentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Detect(Read(evidence, context, model.Thresholds), model);
    }

    public static RecordingPieces Read(SegmentEvidence evidence, SegmentContext context, SegmentThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(thresholds);

        bool[]? sightings = context.Logo.Telling(evidence.Outlines);
        IReadOnlyList<BoundaryCandidate> candidates = BoundaryCandidates.Find(evidence, context, sightings, thresholds);
        (IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<JoinedPiece> joined) = SegmentFeatures.Read(candidates, evidence, context, sightings, thresholds);

        return new RecordingPieces(pieces, BoundaryCandidates.Snaps(candidates), evidence.Length, context.Station, context.Series)
        {
            Joined = joined,
            Candidates = candidates,
        };
    }

    public static SegmentDetection Detect(RecordingPieces read, SegmentModel model)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(model);

        SegmentChances[] chances = [.. read.Pieces.Select(piece => model.Chances(piece.Features, read.Station, read.Series))];
        SegmentChances[] joined = [.. read.Joined.Select(piece => model.Chances(piece.Piece.Features, read.Station, read.Series))];

        return SegmentDetection.Judged(
            SegmentSequence.Decided(read.Pieces, chances, read.Joined, joined, read.Length, model.Thresholds),
            read.Length,
            read.Snaps);
    }
}
